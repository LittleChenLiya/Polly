using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Polly.KeyedCircuitBreaker;

/// <summary>
/// The options for the keyed circuit breaker resilience strategy.
/// </summary>
/// <typeparam name="TResult">The type of result the keyed circuit breaker strategy handles.</typeparam>
/// <remarks>
/// The keyed circuit breaker allows a single resilience pipeline that serves multiple callers (for example multiple tenants)
/// to isolate the failure state of each caller into an independent group identified by a key resolved by <see cref="KeyFactory"/>.
/// <para>
/// Each group tracks its own consecutive failure count. When the count reaches <see cref="FailureThreshold"/>,
/// the circuit for that group breaks and subsequent executions for the same key are rejected with
/// <see cref="KeyedCircuitBreakerBrokenException"/> until <see cref="BreakDuration"/> elapses. The cool-down is evaluated
/// lazily against the <see cref="TimeProvider"/> configured on the resilience pipeline builder; the strategy never starts
/// background timers. A successful (not handled) execution resets the consecutive failure count of the group to zero.
/// Executions that end with an <see cref="OperationCanceledException"/> are neutral: they neither count as failures nor
/// reset the group state.
/// </para>
/// <para>
/// The number of groups is bounded by <see cref="MaxGroups"/>. When the limit is reached and a new key arrives,
/// the strategy evicts the least recently used idle group (a group without executions in flight). If all groups have
/// executions in flight, the new group is rejected with <see cref="KeyedCircuitBreakerCapacityExceededException"/>
/// instead of preempting an active group. Groups never share state with each other, and an evicted group loses all its state.
/// </para>
/// <para>
/// If the <see cref="KeyFactory"/> delegate throws or returns a <see langword="null"/> or empty key, the execution is
/// rejected before the user callback is invoked and the failure is not attributed to any group.
/// </para>
/// </remarks>
public class KeyedCircuitBreakerStrategyOptions<TResult> : ResilienceStrategyOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerStrategyOptions{TResult}"/> class.
    /// </summary>
    public KeyedCircuitBreakerStrategyOptions() => Name = KeyedCircuitBreakerConstants.DefaultName;

    /// <summary>
    /// Gets or sets the factory that resolves the group key for the current execution.
    /// </summary>
    /// <remarks>
    /// The key identifies the group whose failure state is isolated from all other groups.
    /// Use <see cref="ResilienceContext.Properties"/> to flow the caller-provided key (for example a tenant identifier)
    /// into the factory. If the factory throws, the user callback is not executed and the exception
    /// propagates to the caller without affecting any group.
    /// </remarks>
    /// <value>
    /// The default value is <see langword="null"/>. This property is required.
    /// The returned key must be a non-null, non-empty string.
    /// </value>
    [Required]
    public Func<KeyedCircuitBreakerKeyArguments, ValueTask<string>>? KeyFactory { get; set; }

    /// <summary>
    /// Gets or sets a predicate that determines whether the outcome should be handled by the keyed circuit breaker
    /// and counted as a failure of the group.
    /// </summary>
    /// <value>
    /// The default value is a predicate that handles any exception except <see cref="OperationCanceledException"/>.
    /// This property is required.
    /// </value>
    [Required]
    public Func<KeyedCircuitBreakerPredicateArguments<TResult>, ValueTask<bool>> ShouldHandle { get; set; } = DefaultPredicates<KeyedCircuitBreakerPredicateArguments<TResult>, TResult>.HandleOutcome;

    /// <summary>
    /// Gets or sets the number of consecutive handled failures after which the circuit of a group breaks.
    /// </summary>
    /// <value>
    /// The default value is 5. The value must be 1 or greater.
    /// </value>
    [Range(1, int.MaxValue)]
    public int FailureThreshold { get; set; } = KeyedCircuitBreakerConstants.DefaultFailureThreshold;

    /// <summary>
    /// Gets or sets the duration for which the circuit of a group stays broken before new attempts are allowed.
    /// </summary>
    /// <value>
    /// The default value is 5 seconds. The value must be greater than zero.
    /// </value>
    [Range(typeof(TimeSpan), "00:00:00.001", "1.00:00:00")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Addressed with DynamicDependency on ValidationHelper.Validate method")]
    public TimeSpan BreakDuration { get; set; } = KeyedCircuitBreakerConstants.DefaultBreakDuration;

    /// <summary>
    /// Gets or sets the maximum number of groups (distinct keys) the strategy tracks.
    /// </summary>
    /// <remarks>
    /// When the limit is reached, only idle groups (groups without executions in flight) are evicted,
    /// starting from the least recently used one. If no group can be evicted, executions for new keys are
    /// rejected with <see cref="KeyedCircuitBreakerCapacityExceededException"/>.
    /// </remarks>
    /// <value>
    /// The default value is 1024. The value must be 1 or greater.
    /// </value>
    [Range(1, int.MaxValue)]
    public int MaxGroups { get; set; } = KeyedCircuitBreakerConstants.DefaultMaxGroups;
}
