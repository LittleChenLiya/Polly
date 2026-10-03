# Request execution adapter

## About

- **Option(s)**:
  - [`RequestExecutionStrategyOptions<TRequest, TResult>`](xref:Polly.RequestExecution.RequestExecutionStrategyOptions`2)
  - [`VoidRequestExecutionStrategyOptions<TRequest>`](xref:Polly.RequestExecution.VoidRequestExecutionStrategyOptions`1)
  - [`RequestCopyOwnership`](xref:Polly.RequestExecution.RequestCopyOwnership)
- **Extension(s)**:
  - `AddRequestExecution`

---

## Motivation

A retry can accidentally re-send a request object that was already consumed by the previous attempt.
Many request types cannot be executed twice: the content may already have been buffered, headers may
already have been written, or the underlying instance may have been disposed.

The request execution adapter solves this by creating an **explicit, fresh copy** of the original
request for every actual attempt. It is added as the innermost strategy, so every retry or hedging
attempt flows through it:

- the copy factory is invoked **exactly once per attempt**, right before the attempt runs;
- the freshly created copy is passed to the execution callback;
- after the attempt finishes, the copy is released according to the configured ownership;
- the original request always stays owned by the caller and is never disposed or released by the adapter.

This adapter does not change any existing strategy: retries, hedging and the other strategies keep
their default behavior. The adapter is opt-in.

## Usage

<!-- snippet: request-execution -->
```cs
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

```
<!-- endSnippet -->

The terminal callback of `ExecuteAsync` is not used for the actual work in this composition; the
`ExecuteCallback` receives the request copy explicitly.

## Ownership of copies

| Value | Behavior |
| --- | --- |
| `RequestCopyOwnership.Adapter` (default) | The adapter releases the copy after every finished attempt through `ReleaseCallback` (or by disposing the copy when no release callback is provided). |
| `RequestCopyOwnership.Callback` | The execution callback owns the copy and is responsible for releasing it. The adapter never releases copies. |

Even in `Adapter` ownership, the successful result is never released when it **is** the copy:
if a successful attempt returns the request copy itself (or a reference-equal object), ownership of
that copy is transferred to the returned result so the caller keeps a usable object.

<!-- snippet: request-execution-callback-ownership -->
```cs
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

```
<!-- endSnippet -->

## Non-generic (void-returning) pipelines

For pipelines created with the non-generic `ResiliencePipelineBuilder`, use the void variant of the
options whose execution callback returns a `ValueTask` instead of an `Outcome<TResult>`:

<!-- snippet: request-execution-void -->
```cs
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

```
<!-- endSnippet -->

## Factory failures and cancellation

- If `CopyFactory` throws, the exception terminates the execution immediately. The execution callback
  is not invoked, and no additional copy is created. Configure the retry predicate so that only the
  failures reported by the execution callback are retried.
- When an attempt is canceled while a retry is waiting for its next attempt, the wait is abandoned and
  the factory is never invoked for that attempt; no surplus copy is created.
