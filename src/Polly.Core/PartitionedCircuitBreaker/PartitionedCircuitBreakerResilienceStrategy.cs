using Polly.Telemetry;

namespace Polly.Partitioning;

/// <summary>
/// A bounded, partitioned reactive resilience strategy that isolates consecutive failure thresholds by caller-provided key.
/// </summary>
internal sealed class PartitionedCircuitBreakerResilienceStrategy<T> : ResilienceStrategy<T>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PartitionState> _partitions = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly Func<ResilienceContext, string> _keyGenerator;
    private readonly Func<PartitionedCircuitBreakerPredicateArguments<T>, ValueTask<bool>> _shouldHandle;
    private readonly int _failureThreshold;
    private readonly int _maxPartitionCount;
    private readonly TimeSpan _breakDuration;
    private readonly TimeProvider _timeProvider;
    private readonly ResilienceStrategyTelemetry _telemetry;
    private readonly Func<OnPartitionOpenedArguments<T>, ValueTask>? _onOpened;
    private readonly Func<OnPartitionClosedArguments<T>, ValueTask>? _onClosed;

    public PartitionedCircuitBreakerResilienceStrategy(
        Func<ResilienceContext, string> keyGenerator,
        Func<PartitionedCircuitBreakerPredicateArguments<T>, ValueTask<bool>> shouldHandle,
        int failureThreshold,
        TimeSpan breakDuration,
        int maxPartitionCount,
        TimeProvider timeProvider,
        ResilienceStrategyTelemetry telemetry,
        Func<OnPartitionOpenedArguments<T>, ValueTask>? onOpened,
        Func<OnPartitionClosedArguments<T>, ValueTask>? onClosed)
    {
        _keyGenerator = keyGenerator;
        _shouldHandle = shouldHandle;
        _failureThreshold = failureThreshold;
        _breakDuration = breakDuration;
        _maxPartitionCount = maxPartitionCount;
        _timeProvider = timeProvider;
        _telemetry = telemetry;
        _onOpened = onOpened;
        _onClosed = onClosed;
    }

    private sealed class PartitionState
    {
        public int FailureCount;
        public int InFlight;
        public DateTimeOffset BlockedUntil;
        public Outcome<T>? BreakingOutcome;
    }

    protected internal override async ValueTask<Outcome<T>> ExecuteCore<TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<T>>> callback,
        ResilienceContext context,
        TState state)
    {
        // The key factory runs before the user callback. If it throws, the business delegate is never invoked.
        string key = _keyGenerator(context) ?? throw new InvalidOperationException("The partition key generator returned a null key.");

        PartitionState partition;
        lock (_gate)
        {
            if (!_partitions.TryGetValue(key, out partition))
            {
                if (_partitions.Count >= _maxPartitionCount && !TryEvictIdlePartition_NeedsLock())
                {
                    var rejected = new PartitionedCircuitBreakerRejectedException(_maxPartitionCount);
                    _telemetry.SetTelemetrySource(rejected);
                    throw rejected;
                }

                partition = new PartitionState();
                _partitions.Add(key, partition);
                _order.AddLast(key);
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (now < partition.BlockedUntil)
            {
                TimeSpan retryAfter = partition.BlockedUntil - now;
                Exception? breakingException = partition.BreakingOutcome?.Exception;
                var open = breakingException is null
                    ? new PartitionedCircuitBreakerOpenException(PartitionedCircuitBreakerConstants.DefaultOpenMessage, retryAfter)
                    : new PartitionedCircuitBreakerOpenException(PartitionedCircuitBreakerConstants.DefaultOpenMessage, retryAfter, breakingException);
                _telemetry.SetTelemetrySource(open);
                throw open;
            }

            partition.InFlight++;
        }

        Outcome<T> outcome;
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            outcome = await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            outcome = new Outcome<T>(ex);
        }
#pragma warning restore CA1031

        bool handled = await _shouldHandle(new PartitionedCircuitBreakerPredicateArguments<T>(context, outcome))
            .ConfigureAwait(context.ContinueOnCapturedContext);

        Func<ValueTask>? notification = null;
        lock (_gate)
        {
            partition.InFlight--;

            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (now < partition.BlockedUntil)
            {
                // The partition opened while this call was in flight: do not extend or reset the break.
                return outcome;
            }

            if (handled)
            {
                partition.FailureCount++;
                if (partition.FailureCount >= _failureThreshold)
                {
                    partition.BlockedUntil = now + _breakDuration;
                    partition.BreakingOutcome = outcome;

                    var openedArgs = new OnPartitionOpenedArguments<T>(context, outcome, key, _breakDuration);
                    _telemetry.Report(
                        new ResilienceEvent(ResilienceEventSeverity.Error, PartitionedCircuitBreakerConstants.OnPartitionOpened),
                        context,
                        openedArgs);

                    if (_onOpened is not null)
                    {
                        notification = () => _onOpened(openedArgs);
                    }
                }
            }
            else
            {
                bool wasOpen = partition.BlockedUntil != DateTimeOffset.MinValue;
                partition.FailureCount = 0;
                partition.BlockedUntil = DateTimeOffset.MinValue;
                partition.BreakingOutcome = null;

                if (wasOpen)
                {
                    var closedArgs = new OnPartitionClosedArguments<T>(context, outcome, key);
                    _telemetry.Report(
                        new ResilienceEvent(ResilienceEventSeverity.Information, PartitionedCircuitBreakerConstants.OnPartitionClosed),
                        context,
                        closedArgs);

                    if (_onClosed is not null)
                    {
                        notification = () => _onClosed(closedArgs);
                    }
                }
            }
        }

        if (notification is not null)
        {
            await notification().ConfigureAwait(context.ContinueOnCapturedContext);
        }

        return outcome;
    }

    private bool TryEvictIdlePartition_NeedsLock()
    {
        LinkedListNode<string>? node = _order.First;
        while (node is not null)
        {
            string key = node.Value;
            LinkedListNode<string>? next = node.Next;

            if (_partitions.TryGetValue(key, out PartitionState? candidate) && candidate.InFlight == 0)
            {
                _partitions.Remove(key);
                _order.Remove(node);
                return true;
            }

            node = next;
        }

        return false;
    }
}
