using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Polly.OrderedFallback;

namespace Polly;

/// <summary>
/// Extensions for adding ordered fallback to <see cref="ResiliencePipelineBuilder"/>.
/// </summary>
public static class OrderedFallbackResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Adds an ordered fallback resilience strategy with the provided options to the builder.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="builder">The resilience pipeline builder.</param>
    /// <param name="options">The options to configure the ordered fallback resilience strategy.</param>
    /// <returns>The builder instance with the ordered fallback strategy added.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All options members preserved.")]
    public static ResiliencePipelineBuilder<TResult> AddOrderedFallback<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this ResiliencePipelineBuilder<TResult> builder,
        OrderedFallbackStrategyOptions<TResult> options)
    {
        Guard.NotNull(builder);
        Guard.NotNull(options);

        return builder.AddStrategy(context => CreateOrderedFallback(context, options), options);
    }

    private static ResilienceStrategy<TResult> CreateOrderedFallback<TResult>(
        StrategyBuilderContext context,
        OrderedFallbackStrategyOptions<TResult> options)
    {
        // Capture a fixed copy of the candidate list before execution so that external modifications
        // to the options during execution do not cause extra candidates to be run.
        var candidates = options.Candidates.ToList();

        var handler = new OrderedFallbackHandler<TResult>(
            options.ShouldHandle,
            candidates,
            options.OnOrderedFallback);

        return new OrderedFallbackResilienceStrategy<TResult>(
            handler,
            context.Telemetry);
    }
}
