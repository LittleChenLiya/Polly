using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Polly.Partitioning;

namespace Polly;

/// <summary>
/// Partitioned circuit breaker extensions for <see cref="ResiliencePipelineBuilder"/>.
/// </summary>
public static class PartitionedCircuitBreakerResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Adds a partitioned circuit breaker to the builder.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="options">The options instance.</param>
    /// <returns>A builder with the partitioned circuit breaker added.</returns>
    /// <remarks>
    /// See <see cref="PartitionedCircuitBreakerStrategyOptions"/> for more details about the partitioned circuit breaker.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All options members preserved.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PartitionedCircuitBreakerStrategyOptions))]
    public static ResiliencePipelineBuilder AddPartitionedCircuitBreaker(this ResiliencePipelineBuilder builder, PartitionedCircuitBreakerStrategyOptions options)
    {
        Guard.NotNull(builder);
        Guard.NotNull(options);

        return builder.AddStrategy(context => CreateStrategy(context, options), options);
    }

    /// <summary>
    /// Adds a partitioned circuit breaker to the builder.
    /// </summary>
    /// <typeparam name="TResult">The type of result the partitioned circuit breaker handles.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="options">The options instance.</param>
    /// <returns>A builder with the partitioned circuit breaker added.</returns>
    /// <remarks>
    /// See <see cref="PartitionedCircuitBreakerStrategyOptions{TResult}"/> for more details about the partitioned circuit breaker.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All options members preserved.")]
    public static ResiliencePipelineBuilder<TResult> AddPartitionedCircuitBreaker<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this ResiliencePipelineBuilder<TResult> builder,
        PartitionedCircuitBreakerStrategyOptions<TResult> options)
    {
        Guard.NotNull(builder);
        Guard.NotNull(options);

        return builder.AddStrategy(context => CreateStrategy(context, options), options);
    }

    internal static PartitionedCircuitBreakerResilienceStrategy<TResult> CreateStrategy<TResult>(
        StrategyBuilderContext context,
        PartitionedCircuitBreakerStrategyOptions<TResult> options)
    {
        return new PartitionedCircuitBreakerResilienceStrategy<TResult>(
            options.PartitionKeyGenerator!,
            options.ShouldHandle,
            options.FailureThreshold,
            options.BreakDuration,
            options.MaxPartitionCount,
            context.TimeProvider,
            context.Telemetry,
            options.OnOpened,
            options.OnClosed);
    }
}
