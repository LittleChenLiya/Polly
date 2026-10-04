using System.ComponentModel.DataAnnotations;

namespace Polly.OrderedFallback;

/// <summary>
/// Represents a single fallback candidate that can be executed when the primary operation fails.
/// </summary>
/// <typeparam name="TResult">The type of result.</typeparam>
public class OrderedFallbackCandidate<TResult>
{
    /// <summary>
    /// Gets or sets the name of the fallback candidate.
    /// </summary>
    /// <value>The default value is <see langword="null"/>.</value>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the fallback action to be executed when this candidate is selected.
    /// </summary>
    /// <value>The default value is <see langword="null"/>. This property is required.</value>
    [Required]
    public Func<OrderedFallbackActionArguments<TResult>, ValueTask<Outcome<TResult>>>? Action { get; set; }
}
