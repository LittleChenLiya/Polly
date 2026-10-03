using System.ComponentModel.DataAnnotations;

namespace Polly;

/// <summary>
/// The options for the request copy execution adapter.
/// </summary>
/// <typeparam name="TRequest">The type of the request object to copy.</typeparam>
public class RequestCopyResilienceStrategyOptions<TRequest> : ResilienceStrategyOptions
{
    /// <summary>
    /// Gets or sets the factory used to create and release request copies.
    /// </summary>
    [Required]
    public RequestCopyFactory<TRequest>? Factory { get; set; }

    /// <summary>
    /// Gets or sets the ownership mode for the request copy.
    /// </summary>
    /// <value>
    /// The default value is <see cref="RequestCopyOwnership.AdapterOwned"/>.
    /// </value>
    public RequestCopyOwnership Ownership { get; set; } = RequestCopyOwnership.AdapterOwned;
}
