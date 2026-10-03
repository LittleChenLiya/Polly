using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Polly.KeyedCircuitBreaker;

namespace Polly;

/// <summary>
/// Keyed circuit breaker extensions for <see cref="ResiliencePipelineBuilder"/>.
/// </summary>
public static class KeyedCircuitBreakerResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Adds a keyed circuit breaker to the builder.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="options">The options instance.</param>
    /// <returns>A builder with the keyed circuit breaker added.</returns>
    /// <remarks>
    /// See <see cref="KeyedCircuitBreakerStrategyOptions{TResult}"/> for more details about the keyed circuit breaker.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All options members preserved.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(KeyedCircuitBreakerStrategyOptions))]
    public static ResiliencePipelineBuilder AddKeyedCircuitBreaker(this ResiliencePipelineBuilder builder, KeyedCircuitBreakerStrategyOptions options)
    {
        Guard.NotNull(builder);
        Guard.NotNull(options);

        return builder.AddStrategy(context => CreateStrategy(context, options), options);
    }

    /// <summary>
    /// Adds a keyed circuit breaker to the builder.
    /// </summary>
    /// <typeparam name="TResult">The type of result the keyed circuit breaker handles.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="options">The options instance.</param>
    /// <returns>A builder with the keyed circuit breaker added.</returns>
    /// <remarks>
    /// See <see cref="KeyedCircuitBreakerStrategyOptions{TResult}"/> for more details about the keyed circuit breaker.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All options members preserved.")]
    public static ResiliencePipelineBuilder<TResult> AddKeyedCircuitBreaker<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this ResiliencePipelineBuilder<TResult> builder,
        KeyedCircuitBreakerStrategyOptions<TResult> options)
    {
        Guard.NotNull(builder);
        Guard.NotNull(options);

        return builder.AddStrategy(context => CreateStrategy(context, options), options);
    }

    internal static KeyedCircuitBreakerResilienceStrategy<TResult> CreateStrategy<TResult>(
        StrategyBuilderContext context,
        KeyedCircuitBreakerStrategyOptions<TResult> options)
        => new(options, context.TimeProvider);
}
