using System.Diagnostics.CodeAnalysis;
using Polly.RequestExecution;

namespace Polly;

/// <summary>
/// Extensions for adding the request-execution adapter to a resilience pipeline.
/// </summary>
public static class RequestExecutionResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Adds an execution adapter that creates an explicit copy of the original request for every attempt.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request that is executed by the pipeline.</typeparam>
    /// <typeparam name="TResult">The type of the result produced by an execution attempt.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="options">The request-execution options.</param>
    /// <returns>The same builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ComponentModel.DataAnnotations.ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    /// <remarks>
    /// Add this adapter as the innermost strategy, after the retry or hedging strategy:
    /// <code>
    /// new ResiliencePipelineBuilder&lt;HttpResponseMessage&gt;()
    ///     .AddRetry(new RetryStrategyOptions&lt;HttpResponseMessage&gt;())
    ///     .AddRequestExecution(requestOptions)
    ///     .Build();
    /// </code>
    /// Every attempt invokes the copy factory exactly once and executes the resulting copy through
    /// the execution callback. The original request is never released by the adapter.
    /// </remarks>
    [RequiresUnreferencedCode(Constants.OptionsValidation)]
    public static ResiliencePipelineBuilder<TResult> AddRequestExecution<TRequest, TResult>(
        this ResiliencePipelineBuilder<TResult> builder,
        RequestExecutionStrategyOptions<TRequest, TResult> options)
        where TRequest : class
    {
        Guard.NotNull(builder);
        Guard.NotNull(options);

        return builder.AddStrategy(
            _ => new RequestExecutionResilienceStrategy<TRequest, TResult>(options),
            options);
    }

    /// <summary>
    /// Adds an execution adapter that creates an explicit copy of the original request for every attempt
    /// of a non-generic, void-returning pipeline.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request that is executed by the pipeline.</typeparam>
    /// <param name="builder">The builder instance.</param>
    /// <param name="options">The request-execution options.</param>
    /// <returns>The same builder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ComponentModel.DataAnnotations.ValidationException">Thrown when <paramref name="options"/> are invalid.</exception>
    [RequiresUnreferencedCode(Constants.OptionsValidation)]
    public static ResiliencePipelineBuilder AddRequestExecution<TRequest>(
        this ResiliencePipelineBuilder builder,
        VoidRequestExecutionStrategyOptions<TRequest> options)
        where TRequest : class
    {
        Guard.NotNull(builder);
        Guard.NotNull(options);

        return builder.AddStrategy(
            _ => new VoidRequestExecutionResilienceStrategy<TRequest>(options),
            options);
    }
}
