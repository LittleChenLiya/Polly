using Polly.Telemetry;

namespace Polly.Retry;

internal sealed class RetryResilienceStrategy<T> : ResilienceStrategy<T>
{
    private readonly TimeProvider _timeProvider;
    private readonly ResilienceStrategyTelemetry _telemetry;
    private readonly Func<double> _randomizer;

    public RetryResilienceStrategy(
        RetryStrategyOptions<T> options,
        TimeProvider timeProvider,
        ResilienceStrategyTelemetry telemetry)
    {
        ShouldHandle = options.ShouldHandle;
        BaseDelay = options.Delay;
        MaxDelay = options.MaxDelay;
        BackoffType = options.BackoffType;
        RetryCount = options.MaxRetryAttempts;
        OnRetry = options.OnRetry;
        DelayGenerator = options.DelayGenerator;
        UseJitter = options.UseJitter;
        MaxTotalRetryDuration = options.MaxTotalRetryDuration;

        _timeProvider = timeProvider;
        _telemetry = telemetry;
        _randomizer = options.Randomizer;
    }

    public TimeSpan BaseDelay { get; }

    public TimeSpan? MaxDelay { get; }

    public DelayBackoffType BackoffType { get; }

    public int RetryCount { get; }

    public Func<RetryPredicateArguments<T>, ValueTask<bool>> ShouldHandle { get; }

    public Func<RetryDelayGeneratorArguments<T>, ValueTask<TimeSpan?>>? DelayGenerator { get; }

    public bool UseJitter { get; }

    public Func<OnRetryArguments<T>, ValueTask>? OnRetry { get; }

    public TimeSpan? MaxTotalRetryDuration { get; }

    protected internal override async ValueTask<Outcome<T>> ExecuteCore<TState>(Func<ResilienceContext, TState, ValueTask<Outcome<T>>> callback, ResilienceContext context, TState state)
    {
        double retryState = 0;

        int attempt = 0;
        var executionStartTimestamp = _timeProvider.GetTimestamp();

        while (true)
        {
            var startTimestamp = _timeProvider.GetTimestamp();
            Outcome<T> outcome;
            try
            {
                outcome = await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                outcome = new(ex);
            }
#pragma warning restore CA1031

            var shouldRetryArgs = new RetryPredicateArguments<T>(context, outcome, attempt);
            var handle = await ShouldHandle(shouldRetryArgs).ConfigureAwait(context.ContinueOnCapturedContext);
            var executionTime = _timeProvider.GetElapsedTime(startTimestamp);

            var isLastAttempt = IsLastAttempt(attempt, out bool incrementAttempts);
            if (isLastAttempt)
            {
                TelemetryUtil.ReportFinalExecutionAttempt(_telemetry, context, outcome, attempt, executionTime, handle);
            }
            else
            {
                TelemetryUtil.ReportExecutionAttempt(_telemetry, context, outcome, attempt, executionTime, handle);
            }

            if (isLastAttempt || !handle)
            {
                return outcome;
            }

            var delay = RetryHelper.GetRetryDelay(BackoffType, UseJitter, attempt, BaseDelay, MaxDelay, ref retryState, _randomizer);
            if (DelayGenerator is not null)
            {
                var delayArgs = new RetryDelayGeneratorArguments<T>(context, outcome, attempt);

                if (await DelayGenerator(delayArgs).ConfigureAwait(false) is TimeSpan newDelay && RetryHelper.IsValidDelay(newDelay))
                {
                    delay = newDelay;
                }
            }

#pragma warning disable S3236 // Remove this argument from the method call; it hides the caller information.
            Debug.Assert(delay >= TimeSpan.Zero, "The delay cannot be negative.");
#pragma warning restore S3236 // Remove this argument from the method call; it hides the caller information.

            var onRetryArgs = new OnRetryArguments<T>(context, outcome, attempt, delay, executionTime);
            _telemetry.Report<OnRetryArguments<T>, T>(new(ResilienceEventSeverity.Warning, RetryConstants.OnRetryEvent), onRetryArgs);

            if (OnRetry is not null)
            {
                await OnRetry(onRetryArgs).ConfigureAwait(context.ContinueOnCapturedContext);
            }

            TimeSpan elapsed;

            try
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (IsBudgetExceeded(executionStartTimestamp, delay, allowExactFit: true, out elapsed))
                {
                    ReportBudgetExceeded(context, outcome, attempt, elapsed);
                    return outcome;
                }

                // stryker disable once all : no means to test this
                if (delay > TimeSpan.Zero)
                {
                    await _timeProvider.DelayAsync(delay, context).ConfigureAwait(context.ContinueOnCapturedContext);
                }

            }
            catch (OperationCanceledException e)
            {
                return Outcome.FromException<T>(e);
            }

            if (IsBudgetExceeded(executionStartTimestamp, TimeSpan.Zero, allowExactFit: false, out elapsed))
            {
                ReportBudgetExceeded(context, outcome, attempt, elapsed);
                return outcome;
            }

            // The next attempt is really going to happen, so the previous result can be safely discarded.
            // Until this point the last outcome must be preserved, because it is returned to the caller
            // when the total retry duration budget is exhausted.
            if (outcome.TryGetResult(out var resultValue))
            {
                await DisposeHelper.TryDisposeSafeAsync(resultValue, context.IsSynchronous).ConfigureAwait(context.ContinueOnCapturedContext);
            }

            if (incrementAttempts)
            {
                attempt++;
            }
        }
    }

    private bool IsBudgetExceeded(long executionStartTimestamp, TimeSpan delay, bool allowExactFit, out TimeSpan elapsed)
    {
        if (MaxTotalRetryDuration is TimeSpan budget)
        {
            elapsed = _timeProvider.GetElapsedTime(executionStartTimestamp);

            // When allowExactFit is true the remaining budget that exactly fits the delay is still awaited,
            // otherwise (after the delay) any fully consumed budget stops the retry loop.
            return allowExactFit ? elapsed + delay > budget : elapsed + delay >= budget;
        }

        elapsed = TimeSpan.Zero;
        return false;
    }

    private void ReportBudgetExceeded(ResilienceContext context, Outcome<T> outcome, int attempt, TimeSpan elapsed)
    {
        if (MaxTotalRetryDuration is TimeSpan budget)
        {
            _telemetry.Report<RetryBudgetExceededArguments, T>(
                new(ResilienceEventSeverity.Warning, RetryConstants.MaxTotalRetryDurationExceededEvent),
                context,
                outcome,
                new RetryBudgetExceededArguments(attempt, elapsed, budget));
        }
    }

    internal bool IsLastAttempt(int attempt, out bool incrementAttempts)
    {
        if (attempt == int.MaxValue)
        {
            incrementAttempts = false;
            return false;
        }

        incrementAttempts = true;
        return attempt >= RetryCount;
    }
}
