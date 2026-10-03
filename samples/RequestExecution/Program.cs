using Polly;
using Polly.RequestExecution;
using Polly.Retry;

// Demonstrates the request execution adapter: every retry attempt gets a fresh, explicit
// copy of the original request. The first attempt fails, the second succeeds.

var original = new Request("original");
var factoryCalls = 0;
var attempts = 0;
var released = new List<string>();

var pipeline = new ResiliencePipelineBuilder<string>()
    .AddRetry(new RetryStrategyOptions<string>
    {
        ShouldHandle = new PredicateBuilder<string>().Handle<InvalidOperationException>(),
        MaxRetryAttempts = 2,
        Delay = TimeSpan.Zero,
        BackoffType = DelayBackoffType.Constant,
    })
    .AddRequestExecution(new RequestExecutionStrategyOptions<Request, string>
    {
        OriginalRequest = original,
        CopyFactory = (request, _) =>
        {
            var n = ++factoryCalls;
            Console.WriteLine($"Factory: creating copy #{n} from '{request.Name}'");
            return new ValueTask<Request>(new Request($"copy-{n}"));
        },
        ExecuteCallback = (request, _) =>
        {
            attempts++;
            Console.WriteLine($"Attempt {attempts}: executing '{request.Name}'");
            if (attempts == 1)
            {
                return new ValueTask<Outcome<string>>(
                    Outcome.FromException<string>(new InvalidOperationException("transient failure")));
            }

            return new ValueTask<Outcome<string>>(Outcome.FromResult($"handled by {request.Name}"));
        },
        Ownership = RequestCopyOwnership.Adapter,
        ReleaseCallback = request =>
        {
            Console.WriteLine($"Release: releasing '{request.Name}'");
            released.Add(request.Name);
            request.Dispose();
            return default;
        },
    })
    .Build();

var result = await pipeline.ExecuteAsync(
    _ => new ValueTask<string>("unused"),
    CancellationToken.None);

Console.WriteLine($"Result: {result}");
Console.WriteLine($"Factory calls: {factoryCalls}");
Console.WriteLine($"Released copies: {string.Join(", ", released)}");
Console.WriteLine($"Original '{original.Name}' disposed by adapter? {original.IsDisposed}");

// Expected output:
// Factory: creating copy #1 from 'original'
// Attempt 1: executing 'copy-1'
// Release: releasing 'copy-1'
// Factory: creating copy #2 from 'original'
// Attempt 2: executing 'copy-2'
// Release: releasing 'copy-2'
// Result: handled by copy-2
// Factory calls: 2
// Released copies: copy-1, copy-2
// Original 'original' disposed by adapter? False

internal sealed class Request(string name) : IDisposable
{
    public string Name { get; } = name;

    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
