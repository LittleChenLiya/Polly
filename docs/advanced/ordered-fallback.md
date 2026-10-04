# Ordered fallback execution

The `Polly.Fallback.OrderedFallback` class is an independent, ordered fallback execution entry point. When a service call fails and you only want to try a small set of fallback candidates one at a time, `OrderedFallback.ExecuteAsync` invokes them strictly in order and returns the first successful result.

This entry point is standalone: it does not build or modify any `ResiliencePipeline`, and it does not change the behavior of the existing [fallback resilience strategy](../strategies/fallback.md).

## Behavior

- **Fixed snapshot**: the candidates are captured into a fixed snapshot before the execution starts. Mutating the original sequence while the execution is in progress cannot add or remove candidates.
- **At most once**: each candidate is invoked at most once, sequentially, in the configured order. The first successful candidate ends the execution and its result is returned.
- **Caller-judged fallback**: only errors that the caller-provided `isFallbackable` predicate approves move the execution to the next candidate. Any other error stops the execution immediately and is rethrown as-is.
- **Cancellation**: cancellation requested through `ResilienceContext.CancellationToken` stops the execution immediately and is never treated as a fallbackable error.
- **Ordered errors**: if every candidate fails, an `AggregateException` is thrown whose inner exceptions preserve the exact order in which the errors occurred.
- **Empty configuration**: an empty candidate sequence throws an `ArgumentException` before anything runs.

## Usage

```cs
using Polly;
using Polly.Fallback;

ResilienceContext context = ResilienceContextPool.Shared.Get(cancellationToken);
try
{
    var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
    {
        ctx => CallPrimaryAsync(ctx.CancellationToken),
        ctx => CallSecondaryAsync(ctx.CancellationToken),
        ctx => CallLastResortAsync(ctx.CancellationToken),
    };

    // Only errors the caller deems fallbackable move to the next candidate.
    string result = await OrderedFallback.ExecuteAsync(
        context,
        candidates,
        isFallbackable: ex => ex is HttpRequestException or TimeoutException);
}
catch (AggregateException allFailed)
{
    // Every candidate failed; InnerExceptions are in invocation order.
}
finally
{
    ResilienceContextPool.Shared.Return(context);
}
```

## When to use it

- Use `OrderedFallback` when the set of fallback candidates is small, known up front, and must be tried strictly in order with at most one call each.
- Use the [fallback resilience strategy](../strategies/fallback.md) instead when you need a single fallback action integrated into a resilience pipeline with telemetry and outcome-based predicates.
