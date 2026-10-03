using Polly.RequestExecution;
using Polly.Retry;

namespace Snippets.Docs;

internal static class RequestExecution
{
    public sealed class WorkRequest : IDisposable
    {
        public int Sequence { get; set; }

        public bool IsDisposed { get; private set; }

        public WorkRequest Clone() => new() { Sequence = Sequence };

        public void Dispose() => IsDisposed = true;
    }

    public sealed class WorkResponse
    {
        public required WorkRequest Request { get; init; }
    }

    public static async Task ExplicitCopyFactoryAsync()
    {
        #region request-execution

        // The original request is created once and always stays owned by the caller.
        var originalRequest = new WorkRequest();

        var pipeline = new ResiliencePipelineBuilder<WorkResponse>()
            .AddRetry(new RetryStrategyOptions<WorkResponse>
            {
                // Retry only failures produced by the execution callback.
                ShouldHandle = new PredicateBuilder<WorkResponse>().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<WorkRequest, WorkResponse>
            {
                OriginalRequest = originalRequest,

                // Invoked exactly once per actual attempt. If it throws, the execution
                // terminates immediately and the execution callback is not invoked.
                CopyFactory = (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<WorkRequest>(request.Clone());
                },

                // Receives the fresh copy created for this specific attempt.
                ExecuteCallback = (request, context) =>
                {
                    WorkResponse response = PerformAttempt(request);
                    return new ValueTask<Outcome<WorkResponse>>(Outcome.FromResult(response));
                },

                // Default ownership is Adapter, which releases the copy through this callback
                // after every finished attempt.
                Ownership = RequestCopyOwnership.Adapter,
                ReleaseCallback = request =>
                {
                    request.Dispose();
                    return default;
                },
            })
            .Build();

        var response = await pipeline.ExecuteAsync(
            _ => new ValueTask<WorkResponse>((WorkResponse)null!),
            CancellationToken.None);

        // The copy handed back as the successful result is not released by the adapter.
        Console.WriteLine(response.Request.Sequence);

        #endregion
    }

    public static async Task CallbackOwnershipAsync()
    {
        #region request-execution-callback-ownership

        var originalRequest = new WorkRequest();

        var pipeline = new ResiliencePipelineBuilder<WorkResponse>()
            .AddRetry(new RetryStrategyOptions<WorkResponse>())
            .AddRequestExecution(new RequestExecutionStrategyOptions<WorkRequest, WorkResponse>
            {
                OriginalRequest = originalRequest,
                CopyFactory = (request, _) => new ValueTask<WorkRequest>(request.Clone()),

                // The callback owns the copy now, so the adapter never releases it.
                Ownership = RequestCopyOwnership.Callback,
                ExecuteCallback = (request, context) =>
                {
                    // Disposal (if any) is handled inside the callback itself.
                    return new ValueTask<Outcome<WorkResponse>>(Outcome.FromResult(new WorkResponse { Request = request }));
                },
            })
            .Build();

        var response = await pipeline.ExecuteAsync(
            _ => new ValueTask<WorkResponse>((WorkResponse)null!),
            CancellationToken.None);

        Console.WriteLine(response.Request.Sequence);

        #endregion
    }

    public static async Task VoidPipelineAsync()
    {
        #region request-execution-void

        var originalRequest = new WorkRequest();

        // For non-generic, void-returning pipelines use the void variant of the options.
        ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions())
            .AddRequestExecution(new VoidRequestExecutionStrategyOptions<WorkRequest>
            {
                OriginalRequest = originalRequest,
                CopyFactory = (request, _) => new ValueTask<WorkRequest>(request.Clone()),
                Ownership = RequestCopyOwnership.Adapter,
                ReleaseCallback = request =>
                {
                    request.Dispose();
                    return default;
                },
                ExecuteCallback = (request, context) =>
                {
                    PerformAttempt(request);
                    return default;
                },
            })
            .Build();

        await pipeline.ExecuteAsync(_ => default, CancellationToken.None);

        #endregion
    }

    private static WorkResponse PerformAttempt(WorkRequest request) => new() { Request = request };
}
