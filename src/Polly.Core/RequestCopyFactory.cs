namespace Polly;

/// <summary>
/// An abstract factory for creating and releasing copies of a request object.
/// </summary>
/// <typeparam name="TRequest">The type of the request object to copy.</typeparam>
/// <remarks>
/// Implementations of this class are used by the request copy execution adapter to create
/// a fresh copy of the original request for each retry attempt. This prevents the issue where
/// a request object that was already consumed in a previous attempt is reused.
/// </remarks>
public abstract class RequestCopyFactory<TRequest>
{
    /// <summary>
    /// Creates a copy of the specified request.
    /// </summary>
    /// <param name="request">The original request to copy.</param>
    /// <returns>A copy of the request.</returns>
    /// <remarks>
    /// This method is called once per actual attempt by the execution adapter.
    /// If this method throws an exception, the execution is terminated immediately and
    /// no further attempts are made.
    /// </remarks>
    public abstract TRequest CreateCopy(TRequest request);

    /// <summary>
    /// Releases a previously created copy of the request.
    /// </summary>
    /// <param name="copy">The copy to release.</param>
    /// <remarks>
    /// This method is called after each attempt completes when the ownership mode is set to
    /// <see cref="RequestCopyOwnership.AdapterOwned"/>. The default implementation does nothing.
    /// </remarks>
    public virtual void ReleaseCopy(TRequest copy)
    {
    }
}
