namespace Polly;

/// <summary>
/// Defines how the ownership of a request copy is managed by the execution adapter.
/// </summary>
public enum RequestCopyOwnership
{
    /// <summary>
    /// The adapter owns the request copy and releases it after each attempt.
    /// </summary>
    /// <remarks>
    /// When this mode is used, the adapter calls <see cref="RequestCopyFactory{TRequest}.ReleaseCopy"/>
    /// after each attempt completes, regardless of whether the attempt succeeded or failed.
    /// </remarks>
    AdapterOwned,

    /// <summary>
    /// The caller owns the request copy and is responsible for releasing it.
    /// </summary>
    /// <remarks>
    /// When this mode is used, the adapter does not call <see cref="RequestCopyFactory{TRequest}.ReleaseCopy"/>.
    /// The caller is responsible for managing the lifetime of the request copy.
    /// </remarks>
    CallerOwned,
}
