# Partitioned circuit breaker resilience strategy

## About

- **Option(s)**:
  - [`PartitionedCircuitBreakerStrategyOptions`](xref:Polly.Partitioning.PartitionedCircuitBreakerStrategyOptions)
  - [`PartitionedCircuitBreakerStrategyOptions<T>`](xref:Polly.Partitioning.PartitionedCircuitBreakerStrategyOptions`1)
- **Extension(s)**:
  - `AddPartitionedCircuitBreaker`
- **Strategy Type**: Reactive
- **Exception(s)**:
  - [`PartitionedCircuitBreakerOpenException`](xref:Polly.Partitioning.PartitionedCircuitBreakerOpenException): Thrown when a partition is open and the action was not executed.
  - [`PartitionedCircuitBreakerRejectedException`](xref:Polly.Partitioning.PartitionedCircuitBreakerRejectedException): Thrown when a new partition cannot be created because the partition count reached its limit and no idle partition can be evicted.

---

The partitioned circuit breaker **reactive** resilience strategy isolates the failure thresholds of individual partitions (tenants) identified by a caller-provided key. Each partition maintains its own consecutive failure count and cooldown; a failure in one partition never affects any other partition.

The strategy opens a partition after a configurable number of consecutive handled failures. Once open, every call targeting that partition is rejected with a `PartitionedCircuitBreakerOpenException` until the `TimeProvider` of the pipeline advances past the cooldown end. After the cooldown ends, calls are attempted again: a handled failure re-opens the partition, a handled success resets the consecutive failure count.

The number of partitions is bounded by `MaxPartitionCount`. When a new key is encountered and the limit is reached, the strategy evicts the first idle partition (a partition with no in-flight calls, in FIFO order). If no idle partition exists, the new partition is rejected with a `PartitionedCircuitBreakerRejectedException`; an existing partition is never preempted.

> [!NOTE]
> The strategy never starts any background timer. State transitions are driven purely by the `TimeProvider` used by the pipeline, which makes the cooldown deterministic in tests with a fake clock.

## Usage

<!-- snippet: partitioned-circuit-breaker -->
```cs
// Bounded partitioned circuit breaker with default options.
// Each caller-provided key gets its own isolated failure state.
var optionsDefaults = new PartitionedCircuitBreakerStrategyOptions
{
    // Required: extract the partition key (e.g. tenant id) from the context.
    PartitionKeyGenerator = context => context.OperationKey ?? "default"
};

// Partitioned circuit breaker with customized options:
// A partition opens after 2 consecutive handled failures, stays open for 30 seconds
// of the pipeline TimeProvider, and at most 100 partitions are tracked.
var optionsComplex = new PartitionedCircuitBreakerStrategyOptions
{
    PartitionKeyGenerator = context => context.OperationKey ?? "default",
    FailureThreshold = 2,
    BreakDuration = TimeSpan.FromSeconds(30),
    MaxPartitionCount = 100,
    ShouldHandle = new PredicateBuilder().Handle<SomeExceptionType>()
};

// Handle specific failed results for HttpResponseMessage:
var optionsShouldHandle = new PartitionedCircuitBreakerStrategyOptions<HttpResponseMessage>
{
    PartitionKeyGenerator = context => context.OperationKey ?? "default",
    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
        .Handle<SomeExceptionType>()
        .HandleResult(response => response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
};

// Add a partitioned circuit breaker strategy to the pipeline.
// The pipeline TimeProvider (fake clock in tests) drives the cooldown; no background timers are started.
new ResiliencePipelineBuilder().AddPartitionedCircuitBreaker(optionsDefaults);
new ResiliencePipelineBuilder<HttpResponseMessage>().AddPartitionedCircuitBreaker(optionsShouldHandle);
```
<!-- endSnippet -->

### Failure handling

The partitioned circuit breaker returns the result / exception of the executed delegate. Once a partition opens, every subsequent call targeting that partition is shortcut with a `PartitionedCircuitBreakerOpenException`. Cancellation outcomes (exceptions deriving from `OperationCanceledException`) are not handled by the default predicate and therefore do not count towards the failure threshold.

The partition key generator is invoked before the user callback. If the key generator throws, the user callback is never invoked and the exception propagates to the caller.

## Defaults

| Property              | Default Value                                           | Description                                                                                                                          |
|-----------------------|---------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------|
| `PartitionKeyGenerator` | `null`                                               | A required delegate that generates the partition key for the specified resilience context.                                           |
| `ShouldHandle`        | Any exceptions other than `OperationCanceledException`. | Defines a predicate to determine what results and/or exceptions are handled by the partitioned circuit breaker.                       |
| `FailureThreshold`     | 5                                                       | The number of consecutive handled failures after which the partition opens.                                                           |
| `BreakDuration`        | 5 seconds                                               | The duration for which a partition stays open before a call is attempted again, measured by the pipeline `TimeProvider`.              |
| `MaxPartitionCount`    | 100                                                     | The maximum number of partitions the strategy can track at the same time. When the limit is reached, idle partitions are evicted.   |
| `OnOpened`             | `null`                                                  | If provided then it will be invoked after a partition transitions to the open state.                                                  |
| `OnClosed`             | `null`                                                  | If provided then it will be invoked after a partition resets to the closed state.                                                     |

## Telemetry

The partitioned circuit breaker strategy reports the following telemetry events:

| Event Name              | Event Severity | When?                                                   |
|-------------------------|----------------|---------------------------------------------------------|
| `OnPartitionOpened`     | `Error`        | Just before the strategy calls the `OnOpened` delegate. |
| `OnPartitionClosed`     | `Information`  | Just before the strategy calls the `OnClosed` delegate. |

For further information please check out the [telemetry page](../advanced/telemetry.md).
