using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Polly.RequestExecution;

/// <summary>
/// Options for the request-execution adapter used with a non-generic, void-returning pipeline.
/// </summary>
/// <typeparam name="TRequest">The type of the request that is executed by the pipeline.</typeparam>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Options contain no members requiring dynamic access.")]
public sealed class VoidRequestExecutionStrategyOptions<TRequest> : ResilienceStrategyOptions
    where TRequest : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VoidRequestExecutionStrategyOptions{TRequest}"/> class.
    /// </summary>
    public VoidRequestExecutionStrategyOptions() => Name = RequestExecutionConstants.DefaultName;

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
    /// The factory is invoked exactly once per actual attempt. If the factory throws, the execution
    /// terminates immediately and <see cref="ExecuteCallback"/> is not invoked for that attempt.
    /// </remarks>
    [Required]
    public Func<TRequest, CancellationToken, ValueTask<TRequest>>? CopyFactory { get; set; }

    /// <summary>
    /// Gets or sets the callback that performs a single, void-returning attempt with the copy created for that attempt.
    /// </summary>
    [Required]
    public Func<TRequest, ResilienceContext, ValueTask>? ExecuteCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback that releases a request copy once an attempt is finished.
    /// </summary>
    /// <remarks>
    /// The release callback is invoked only when <see cref="Ownership"/> is
    /// <see cref="RequestCopyOwnership.Adapter"/>.
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
