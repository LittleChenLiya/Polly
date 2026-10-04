using Polly;
using Polly.Fallback;

// Demo of Polly.Fallback.OrderedFallback: an independent, ordered fallback execution entry point.
// After the primary call fails, a small set of fallback candidates is tried one at a time, in order.

static bool IsTransient(Exception e) => e is HttpRequestException or TimeoutException;

// Scenario 1: fallbackable failure -> success -> (never executed)
Console.WriteLine("--- Scenario 1: first success ends the execution ---");
var context = ResilienceContextPool.Shared.Get();
try
{
    var thirdCalls = 0;
    var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
    {
        _ => throw new HttpRequestException("primary endpoint down"),
        _ => new ValueTask<string>("response from secondary endpoint"),
        _ => { thirdCalls++; return new ValueTask<string>("response from tertiary endpoint"); },
    };

    var result = await OrderedFallback.ExecuteAsync(context, candidates, IsTransient);
    Console.WriteLine($"Result: {result}");
    Console.WriteLine($"Third candidate invocations: {thirdCalls} (expected 0)");
}
finally
{
    ResilienceContextPool.Shared.Return(context);
}

// Scenario 2: a non-fallbackable error stops the execution immediately
Console.WriteLine();
Console.WriteLine("--- Scenario 2: non-fallbackable error stops immediately ---");
context = ResilienceContextPool.Shared.Get();
try
{
    var secondCalls = 0;
    var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
    {
        _ => throw new InvalidOperationException("fatal, not fallbackable"),
        _ => { secondCalls++; return new ValueTask<string>("never reached"); },
    };

    try
    {
        await OrderedFallback.ExecuteAsync(context, candidates, IsTransient);
    }
    catch (InvalidOperationException e)
    {
        Console.WriteLine($"Stopped with: {e.GetType().Name}: {e.Message}");
        Console.WriteLine($"Second candidate invocations: {secondCalls} (expected 0)");
    }
}
finally
{
    ResilienceContextPool.Shared.Return(context);
}

// Scenario 3: all candidates fail - errors are preserved in invocation order
Console.WriteLine();
Console.WriteLine("--- Scenario 3: all fail, errors preserved in order ---");
context = ResilienceContextPool.Shared.Get();
try
{
    var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
    {
        _ => throw new HttpRequestException("attempt 1 failed"),
        _ => throw new TimeoutException("attempt 2 failed"),
        _ => throw new HttpRequestException("attempt 3 failed"),
    };

    try
    {
        await OrderedFallback.ExecuteAsync(context, candidates, IsTransient);
    }
    catch (AggregateException e)
    {
        Console.WriteLine("Errors in order: " + string.Join(" | ", e.InnerExceptions.Select(x => x.Message)));
    }
}
finally
{
    ResilienceContextPool.Shared.Return(context);
}

// Scenario 4: cancellation stops the execution immediately
Console.WriteLine();
Console.WriteLine("--- Scenario 4: cancellation stops immediately ---");
using var cts = new CancellationTokenSource();
context = ResilienceContextPool.Shared.Get(cts.Token);
try
{
    var secondCalls = 0;
    var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
    {
        _ => { cts.Cancel(); throw new HttpRequestException("transient, but cancellation follows"); },
        _ => { secondCalls++; return new ValueTask<string>("never reached"); },
    };

    try
    {
        await OrderedFallback.ExecuteAsync(context, candidates, IsTransient);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"Canceled. Second candidate invocations: {secondCalls} (expected 0)");
    }
}
finally
{
    ResilienceContextPool.Shared.Return(context);
}
