namespace Polly;

/// <summary>
/// Provides the property keys used to store the original request and request copy in the resilience context.
/// </summary>
internal static class RequestCopyResilienceStrategy
{
    /// <summary>
    /// The property key used to store the original request in the resilience context.
    /// </summary>
    public static readonly ResiliencePropertyKey<object?> OriginalRequestKey = new("Polly.RequestCopy.OriginalRequest");

    /// <summary>
    /// The property key used to store the request copy in the resilience context.
    /// </summary>
    public static readonly ResiliencePropertyKey<object?> RequestCopyKey = new("Polly.RequestCopy.RequestCopy");
}

/// <summary>
/// A resilience strategy that creates a copy of the request for each attempt and releases it after the attempt.
/// </summary>
/// <typeparam name="TRequest">The type of the request object to copy.</typeparam>
/// <remarks>
/// <para>
/// This strategy is designed to be used inside a retry pipeline. It is placed below the retry strategy
/// so that the retry's loop invokes it once per attempt. The strategy calls the factory to create a copy
/// of the original request, hands the copy to the execution callback, and releases the copy after the attempt
/// completes (when ownership is set to <see cref="RequestCopyOwnership.AdapterOwned"/>).
/// </para>
/// <para>
/// If the factory fails to create a copy, the execution is terminated immediately and no further attempts
/// are made. The original request is always held by the caller and is not automatically disposed.
/// </para>
/// </remarks>
internal sealed class RequestCopyResilienceStrategy<TRequest> : ResilienceStrategy
{
    private readonly RequestCopyFactory<TRequest> _factory;
    private readonly RequestCopyOwnership _ownership;

    public RequestCopyResilienceStrategy(RequestCopyFactory<TRequest> factory, RequestCopyOwnership ownership)
    {
        _factory = factory;
        _ownership = ownership;
    }

    /// <inheritdoc/>
    protected internal override async ValueTask<Outcome<TResult>> ExecuteCore<TResult, TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context,
        TState state)
    {
        TRequest copy;
#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            copy = _factory.CreateCopy(GetOriginalRequest(context));
            context.Properties.Set(RequestCopyResilienceStrategy.RequestCopyKey, copy);
        }
        catch (Exception ex)
        {
            // Factory failure terminates the execution immediately.
            // We wrap the exception in an OperationCanceledException so that the retry's
            // default ShouldHandle predicate (which handles all exceptions except OCE) does not retry it.
            return Outcome.FromException<TResult>(new OperationCanceledException(
                "The request copy factory failed to create a copy of the request. See the inner exception for details.",
                ex));
        }
#pragma warning restore CA1031 // Do not catch general exception types

        try
        {
            return await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
        }
        finally
        {
            if (_ownership == RequestCopyOwnership.AdapterOwned)
            {
                _factory.ReleaseCopy(copy);
            }
        }
    }

    private static TRequest GetOriginalRequest(ResilienceContext context)
    {
        if (context.Properties.TryGetValue<object?>(RequestCopyResilienceStrategy.OriginalRequestKey, out var request)
            && request is TRequest typedRequest)
        {
            return typedRequest;
        }

        throw new InvalidOperationException(
#pragma warning disable S2302 // Replace the string 'context' with 'nameof(context)'
            $"The original request was not found in the resilience context. " +
#pragma warning restore S2302 // Replace the string 'context' with 'nameof(context)'
            $"Ensure the request copy execution adapter is used with a pipeline that provides the original request.");
    }
}
