using Polly;
using Polly.Retry;

// Simulates an HTTP request message that can only be consumed once.
public class HttpRequestMessage
{
    public string? Content { get; set; }
    public bool IsConsumed { get; private set; }

    public void Consume()
    {
        if (IsConsumed)
        {
            throw new InvalidOperationException("Request message has already been consumed.");
        }

        IsConsumed = true;
    }
}

// Factory that creates copies of the request message.
public class HttpRequestMessageCopyFactory : RequestCopyFactory<HttpRequestMessage>
{
    private int _copyCount;

    public override HttpRequestMessage CreateCopy(HttpRequestMessage request)
    {
        _copyCount++;
        Console.WriteLine($"  [Factory] Creating copy #{_copyCount}");
        return new HttpRequestMessage { Content = request.Content };
    }

    public override void ReleaseCopy(HttpRequestMessage copy) => Console.WriteLine($"  [Factory] Releasing copy");
}

public class Program
{
    public static async Task Main()
    {
        Console.WriteLine("=== Request Copy Demo ===");
        Console.WriteLine();

        var factory = new HttpRequestMessageCopyFactory();

        // Build a pipeline with retry and request copy.
        // The request copy strategy is placed after retry so it is invoked once per attempt.
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = _ => PredicateResult.True(),
            })
            .AddRequestCopy(new RequestCopyResilienceStrategyOptions<HttpRequestMessage>
            {
                Factory = factory,
                Ownership = RequestCopyOwnership.AdapterOwned,
            })
            .Build();

        var executor = new RequestCopyExecutor<HttpRequestMessage>(pipeline, factory);

        var originalRequest = new HttpRequestMessage { Content = "Hello, World!" };
        var attemptCount = 0;

        Console.WriteLine("Executing with original request...");
        Console.WriteLine($"  Original request consumed: {originalRequest.IsConsumed}");
        Console.WriteLine();

        try
        {
            var result = await executor.ExecuteAsync(
                originalRequest,
                (context, request, cancellationToken) =>
                {
                    attemptCount++;
                    Console.WriteLine($"  Attempt {attemptCount}: Using request copy (consumed: {request.IsConsumed})");

                    // Simulate consuming the request.
                    request.Consume();

                    if (attemptCount < 3)
                    {
                        Console.WriteLine($"  Attempt {attemptCount}: Failed, retrying...");
                        throw new InvalidOperationException("Simulated failure");
                    }

                    Console.WriteLine($"  Attempt {attemptCount}: Succeeded!");
                    return new ValueTask<string>("Success");
                });

            Console.WriteLine();
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Execution failed: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine($"Original request consumed: {originalRequest.IsConsumed}");
        Console.WriteLine("=== Demo Complete ===");
    }
}
