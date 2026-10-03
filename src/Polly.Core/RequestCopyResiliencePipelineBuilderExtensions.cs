using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Polly;

/// <summary>
/// Extensions for adding request copy strategies to <see cref="ResiliencePipelineBuilder"/>.
/// </summary>
public static class RequestCopyResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Adds a request copy strategy to the builder.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request object to copy.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="options">The request copy options.</param>
    /// <returns>The builder instance with the request copy strategy added.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    /// <remarks>
    /// <para>
    /// The request copy strategy should be added <strong>after</strong> the retry strategy in the pipeline
    /// so that it is placed below the retry in the execution chain. This ensures that the retry's loop
    /// invokes the strategy once per attempt.
    /// </para>
    /// <para>
    /// The original request must be provided through the <see cref="RequestCopyExecutor{TRequest}"/>
    /// when executing the pipeline.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All options members preserved.")]
    public static ResiliencePipelineBuilder AddRequestCopy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(
        this ResiliencePipelineBuilder builder,
        RequestCopyResilienceStrategyOptions<TRequest> options)
    {
        Guard.NotNull(builder);
        Guard.NotNull(options);

        return builder.AddStrategy(
            context => new RequestCopyResilienceStrategy<TRequest>(options.Factory!, options.Ownership),
            options);
    }
}
