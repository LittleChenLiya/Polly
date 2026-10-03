namespace Polly;

/// <summary>
/// An execution adapter that creates a copy of the request for each retry attempt.
/// </summary>
/// <typeparam name="TRequest">The type of the request object to copy.</typeparam>
/// <remarks>
/// <para>
/// This adapter is designed for retry scenarios where the original request object might be consumed
/// in a previous attempt and cannot be reused. The adapter creates a fresh copy of the request for each
/// actual attempt and hands the copy to the execution callback.
/// </para>
/// <para>
/// The original request is always held by the caller and is not automatically disposed.
/// The adapter only manages the lifetime of the copies it creates.
/// </para>
/// </remarks>
public sealed class RequestCopyExecutor<TRequest>
{
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestCopyExecutor{TRequest}"/> class.
    /// </summary>
    /// <param name="pipeline">The resilience pipeline to execute.</param>
    /// <param name="factory">The factory used to create and release request copies.</param>
    /// <param name="ownership">The ownership mode for the request copy.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pipeline"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
#pragma warning disable IDE0060 // Remove unused parameter
    public RequestCopyExecutor(
        ResiliencePipeline pipeline,
        RequestCopyFactory<TRequest> factory,
        RequestCopyOwnership ownership = RequestCopyOwnership.AdapterOwned)
    {
        Guard.NotNull(pipeline);
        Guard.NotNull(factory);

        _pipeline = pipeline;
    }
#pragma warning restore IDE0060 // Remove unused parameter

    /// <summary>
    /// Executes the specified callback with a copy of the request.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="request">The original request to copy.</param>
    /// <param name="callback">The execution callback that receives the request copy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the execution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    /// <exception cref="RequestCopyFactoryException">Thrown when the factory fails to create a copy.</exception>
    /// <remarks>
    /// The factory is called once per actual attempt. If the factory fails to create a copy,
    /// the execution is terminated immediately and no further attempts are made.
    /// </remarks>
    public async ValueTask<TResult> ExecuteAsync<TResult>(
        TRequest request,
        Func<ResilienceContext, TRequest, CancellationToken, ValueTask<TResult>> callback,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(callback);

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        try
        {
            context.Properties.Set(RequestCopyResilienceStrategy.OriginalRequestKey, request);

            var outcome = await _pipeline.ExecuteOutcomeAsync<TResult, (Func<ResilienceContext, TRequest, CancellationToken, ValueTask<TResult>> Callback, TRequest Request)>(
                static async (ctx, state) =>
                {
                    var request = ctx.Properties.TryGetValue<object?>(RequestCopyResilienceStrategy.RequestCopyKey, out var copy)
                        && copy is TRequest typedCopy
                        ? typedCopy
                        : state.Request;
                    return Outcome.FromResult(await state.Callback(ctx, request, ctx.CancellationToken).ConfigureAwait(ctx.ContinueOnCapturedContext));
                },
                context,
                (Callback: callback, Request: request)).ConfigureAwait(context.ContinueOnCapturedContext);

            return outcome.GetResultOrRethrow();
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
