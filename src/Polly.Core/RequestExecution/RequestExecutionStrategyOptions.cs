using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Polly.RequestExecution;

/// <summary>
/// Options for the request-execution adapter that creates an explicit copy of the original request
/// for every attempt.
/// </summary>
/// <typeparam name="TRequest">The type of the request that is executed by the pipeline.</typeparam>
/// <typeparam name="TResult">The type of the result produced by an execution attempt.</typeparam>
/// <remarks>
/// <para>
/// Retries must never reuse a request object that an earlier attempt already consumed, as a consumed
/// request (such as a buffered <c>HttpRequestMessage</c>) usually cannot be sent a second time.
/// The request-execution adapter invokes the <see cref="CopyFactory"/> exactly once per actual attempt
/// and hands the freshly created copy to the <see cref="ExecuteCallback"/>.
/// </para>
/// <para>
/// The original request is always retained by the caller and is never released by the adapter.
/// Use <see cref="ReleaseCallback"/> together with <see cref="Ownership"/> to control how (and by whom)
/// the per-attempt copies are released.
/// </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Options contain no members requiring dynamic access.")]
public sealed class RequestExecutionStrategyOptions<TRequest, TResult> : ResilienceStrategyOptions
    where TRequest : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestExecutionStrategyOptions{TRequest, TResult}"/> class.
    /// </summary>
    public RequestExecutionStrategyOptions() => Name = RequestExecutionConstants.DefaultName;

    /// <summary>
    /// Gets or sets the original request that is copied for every attempt.
    /// </summary>
    /// <remarks>
    /// The original request is always owned by the caller. The adapter only reads it through the
    /// <see cref="CopyFactory"/> and never releases it.
    /// </remarks>
    [Required]
    public TRequest? OriginalRequest { get; set; }

    /// <summary>
    /// Gets or sets the factory that creates a fresh copy of the <see cref="OriginalRequest"/>
    /// for a single attempt.
    /// </summary>
    /// <remarks>
    /// The factory is invoked exactly once per actual attempt. If the factory throws, the exception
    /// terminates the execution immediately and <see cref="ExecuteCallback"/> is not invoked for that
    /// attempt; no extra copies are created.
    /// </remarks>
    [Required]
    public Func<TRequest, CancellationToken, ValueTask<TRequest>>? CopyFactory { get; set; }

    /// <summary>
    /// Gets or sets the callback that performs a single attempt using the request copy created for that attempt.
    /// </summary>
    [Required]
    public Func<TRequest, ResilienceContext, ValueTask<Outcome<TResult>>>? ExecuteCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback that releases a request copy once an attempt is finished.
    /// </summary>
    /// <remarks>
    /// The release callback is invoked only when <see cref="Ownership"/> is
    /// <see cref="RequestCopyOwnership.Adapter"/>. It is not invoked for the successful attempt when
    /// the attempt returns the copy itself, because in that case ownership of the copy is transferred
    /// to the returned result.
    /// </remarks>
    public Func<TRequest, ValueTask>? ReleaseCallback { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the adapter or the execution callback owns the
    /// per-attempt copy.
    /// </summary>
    /// <value>
    /// The default value is <see cref="RequestCopyOwnership.Adapter"/>.
    /// </value>
    public RequestCopyOwnership Ownership { get; set; } = RequestCopyOwnership.Adapter;
}
