using Polly.Telemetry;
using Polly.Utils;

namespace Polly.OrderedFallback;

#pragma warning disable CA1031 // Do not catch general exception types

internal sealed class OrderedFallbackResilienceStrategy<T> : ResilienceStrategy<T>
{
    private readonly OrderedFallbackHandler<T> _handler;
    private readonly ResilienceStrategyTelemetry _telemetry;

    public OrderedFallbackResilienceStrategy(OrderedFallbackHandler<T> handler, ResilienceStrategyTelemetry telemetry)
    {
        _handler = handler;
        _telemetry = telemetry;
    }

    protected internal override async ValueTask<Outcome<T>> ExecuteCore<TState>(Func<ResilienceContext, TState, ValueTask<Outcome<T>>> callback, ResilienceContext context, TState state)
    {
        Outcome<T> outcome;
        try
        {
            outcome = await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
        }
        catch (Exception ex)
        {
            outcome = new(ex);
        }

        return await ExecuteWithOrderedFallbackAsync(context, outcome).ConfigureAwait(context.ContinueOnCapturedContext);
    }

    private async ValueTask<Outcome<T>> ExecuteWithOrderedFallbackAsync(ResilienceContext context, Outcome<T> primaryOutcome)
    {
        var outcome = primaryOutcome;

        var shouldHandleArgs = new OrderedFallbackPredicateArguments<T>(context, outcome);
        if (!await _handler.ShouldHandle(shouldHandleArgs).ConfigureAwait(context.ContinueOnCapturedContext))
        {
            return outcome;
        }

        var exceptions = new List<Exception>();

        foreach (var candidate in _handler.Candidates)
        {
            // Reuse ResilienceContext cancellation semantics: stop immediately if cancellation is requested.
            if (context.CancellationToken.IsCancellationRequested)
            {
                var canceledException = new OperationCanceledException(context.CancellationToken).TrySetStackTrace();
                exceptions.Add(canceledException);
                return Outcome.FromException<T>(new AggregateException(exceptions));
            }

            var onOrderedFallbackArgs = new OnOrderedFallbackArguments<T>(context, outcome, candidate.Name);

            _telemetry.Report<OnOrderedFallbackArguments<T>, T>(new(ResilienceEventSeverity.Warning, OrderedFallbackConstants.OnOrderedFallback), onOrderedFallbackArgs);

            if (_handler.OnOrderedFallback is not null)
            {
                await _handler.OnOrderedFallback(onOrderedFallbackArgs).ConfigureAwait(context.ContinueOnCapturedContext);
            }

            try
            {
                var candidateOutcome = await candidate.Action!(new OrderedFallbackActionArguments<T>(context, outcome)).ConfigureAwait(context.ContinueOnCapturedContext);

                // First success ends the fallback chain.
                if (candidateOutcome.Exception is null)
                {
                    return candidateOutcome;
                }

                // Only errors that the caller determines are fallbackable enter the next candidate.
                if (!await _handler.ShouldHandle(new OrderedFallbackPredicateArguments<T>(context, candidateOutcome)).ConfigureAwait(context.ContinueOnCapturedContext))
                {
                    return candidateOutcome;
                }

                outcome = candidateOutcome;
                exceptions.Add(candidateOutcome.Exception!);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
                outcome = new(ex);
            }
        }

        // All candidates failed: preserve errors in the actual call order (sequential, not parallel).
        if (exceptions.Count > 0)
        {
            return Outcome.FromException<T>(new AggregateException(exceptions));
        }

        return outcome;
    }
}
