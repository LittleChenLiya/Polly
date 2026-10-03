# Request copy resilience strategy

## About

- **Option(s)**:
  - [`RequestCopyResilienceStrategyOptions<TRequest>`](xref:Polly.RequestCopyResilienceStrategyOptions`1)
- **Extension(s)**:
  - `AddRequestCopy`
- **Exception(s)**:
  - [`RequestCopyFactoryException`](xref:Polly.RequestCopyFactoryException)

---

The request copy **execution adapter** creates a fresh copy of the request object for each retry attempt. This prevents the issue where a request object that was already consumed in a previous attempt is reused. The adapter is designed to be used inside a retry pipeline, where it is placed below the retry strategy so that the retry's loop invokes it once per attempt.

## Usage

<!-- snippet: request-copy -->
```cs
// Create a request copy factory
var factory = new MyRequestCopyFactory();

// Build a pipeline with retry and request copy
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        ShouldHandle = _ => PredicateResult.True(),
    })
    .AddRequestCopy(new RequestCopyResilienceStrategyOptions<HttpRequestMessage>
    {
        Factory = factory,
        Ownership = RequestCopyOwnership.AdapterOwned,
    })
    .Build();

// Create an executor and execute with the original request
var executor = new RequestCopyExecutor<HttpRequestMessage>(pipeline, factory);

var result = await executor.ExecuteAsync(
    originalRequest,
    (context, request, cancellationToken) =>
    {
        // Use the request copy in the callback
        return SendRequestAsync(request, cancellationToken);
    },
    cancellationToken);
```
<!-- endSnippet -->

## Ownership modes

The request copy adapter supports two ownership modes:

- **AdapterOwned**: The adapter owns the request copy and releases it after each attempt. This is the default mode.
- **CallerOwned**: The caller owns the request copy and is responsible for releasing it. The adapter does not call `ReleaseCopy`.

## Factory failure handling

If the factory's `CreateCopy` method throws an exception, the execution is terminated immediately and no further attempts are made. The factory exception is wrapped in an `OperationCanceledException` so that the retry's default `ShouldHandle` predicate (which handles all exceptions except `OperationCanceledException`) does not retry it.

## Original request lifetime

The original request is always held by the caller and is not automatically disposed. The adapter only manages the lifetime of the copies it creates. The successfully returned result is not affected by the request cleanup.

## Fake clock support

The request copy adapter works with `FakeTimeProvider` for testing time-dependent behavior. When a retry is cancelled during the retry wait, no additional copies are created.
