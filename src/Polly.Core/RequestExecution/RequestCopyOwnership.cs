namespace Polly.RequestExecution;

/// <summary>
/// Determines who owns the request copy produced by the request-copy factory and is responsible
/// for releasing it once an attempt is finished.
/// </summary>
public enum RequestCopyOwnership
{
    /// <summary>
    /// The execution adapter owns the copy and releases it after every attempt.
    /// </summary>
    /// <remarks>
    /// When the successful attempt returns the copy itself (or an object that is reference-equal to it),
    /// ownership is transferred to the caller and the copy is not released by the adapter, so the returned
    /// result stays usable.
    /// </remarks>
    Adapter,

    /// <summary>
    /// The execution callback owns the copy and is responsible for releasing it.
    /// </summary>
    /// <remarks>
    /// The adapter never releases the copy in this mode. Use it when the callback already disposes the
    /// request itself (for example through an <c>using</c> statement) or when the request is forwarded
    /// to another component that takes ownership of it.
    /// </remarks>
    Callback,
}
