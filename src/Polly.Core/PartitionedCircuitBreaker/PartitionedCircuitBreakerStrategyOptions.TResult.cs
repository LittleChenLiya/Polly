using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Polly.Partitioning;

/// <summary>
/// The options for the partitioned circuit breaker resilience strategy.
/// </summary>
/// <typeparam name="TResult">The type of result the partitioned circuit breaker handles.</typeparam>
/// <remarks>
/// <para>
/// The partitioned circuit breaker isolates the failure thresholds of individual partitions (tenants) identified
/// by a caller-provided key. Each partition maintains its own state: a partition opens after
/// <see cref="FailureThreshold"/> consecutive handled failures and stays open until the explicit
/// <see cref="TimeProvider"/> provided by the builder advances past the cooldown end. A handled success resets
/// the consecutive failure count of the partition.
/// </para>
/// <para>
/// The number of partitions is bounded by <see cref="MaxPartitionCount"/>. When a new key is encountered and the
/// limit is reached, only idle partitions (partitions with no in-flight calls) are eligible for eviction. If no
/// idle partition exists, the new partition is rejected with a
/// <see cref="PartitionedCircuitBreakerRejectedException"/>; an existing partition is never preempted.
/// </para>
/// <para>
/// The strategy never starts any background timer. State transitions are driven purely by the
/// <see cref="TimeProvider"/> used by the pipeline.
/// </para>
/// </remarks>
public class PartitionedCircuitBreakerStrategyOptions<TResult> : ResilienceStrategyOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerStrategyOptions{TResult}"/> class.
    /// </summary>
    public PartitionedCircuitBreakerStrategyOptions() => Name = PartitionedCircuitBreakerConstants.DefaultName;

    /// <summary>
    /// Gets or sets the delegate that generates the partition key for the specified resilience context.
    /// </summary>
    /// <value>
    /// The default value is <see langword="null"/>. This property is required.
    /// </value>
    /// <remarks>
    /// The delegate is invoked before the user callback. If it throws an exception, the user callback is never
    /// invoked and the exception propagates to the caller.
    /// </remarks>
    [Required]
    public Func<ResilienceContext, string>? PartitionKeyGenerator { get; set; }

    /// <summary>
    /// Gets or sets the number of consecutive handled failures after which the partition opens.
    /// </summary>
    /// <value>
    /// The default value is 5. The value must be 1 or greater.
    /// </value>
    [Range(1, int.MaxValue)]
    public int FailureThreshold { get; set; } = PartitionedCircuitBreakerConstants.DefaultFailureThreshold;

    /// <summary>
    /// Gets or sets the duration for which a partition stays open before a call is attempted again.
    /// </summary>
    /// <value>
    /// The default value is 5 seconds. Value must be greater than 0.5 seconds.
    /// </value>
    [Range(typeof(TimeSpan), "00:00:00.500", "1.00:00:00")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Addressed with DynamicDependency on ValidationHelper.Validate method")]
    public TimeSpan BreakDuration { get; set; } = PartitionedCircuitBreakerConstants.DefaultBreakDuration;

    /// <summary>
    /// Gets or sets the maximum number of partitions the strategy can track at the same time.
    /// </summary>
    /// <value>
    /// The default value is 100. The value must be 1 or greater.
    /// </value>
    /// <remarks>
    /// When a new key is encountered and the limit is reached, the strategy evicts the first idle partition
    /// (a partition with no in-flight calls). If no idle partition exists, the new partition is rejected.
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int MaxPartitionCount { get; set; } = PartitionedCircuitBreakerConstants.DefaultMaxPartitionCount;

    /// <summary>
    /// Gets or sets a predicate that determines whether the outcome should be handled by the partitioned circuit breaker.
    /// </summary>
    /// <value>
    /// The default value is a predicate that handles any exception except <see cref="OperationCanceledException"/>.
    /// This property is required.
    /// </value>
    [Required]
    public Func<PartitionedCircuitBreakerPredicateArguments<TResult>, ValueTask<bool>> ShouldHandle { get; set; }
        = DefaultPredicates<PartitionedCircuitBreakerPredicateArguments<TResult>, TResult>.HandleOutcome;

    /// <summary>
    /// Gets or sets the event that is raised when a partition transitions to the open state.
    /// </summary>
    /// <value>The default value is <see langword="null"/>.</value>
    public Func<OnPartitionOpenedArguments<TResult>, ValueTask>? OnOpened { get; set; }

    /// <summary>
    /// Gets or sets the event that is raised when a partition resets to the closed state.
    /// </summary>
    /// <value>The default value is <see langword="null"/>.</value>
    public Func<OnPartitionClosedArguments<TResult>, ValueTask>? OnClosed { get; set; }
}
