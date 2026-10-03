using System.Net.Http;
using Polly.Partitioning;
using Snippets.Docs.Utils;

namespace Snippets.Docs;

internal static class PartitionedCircuitBreaker
{
    public static void Usage()
    {
        #region partitioned-circuit-breaker

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

        #endregion
    }
}
