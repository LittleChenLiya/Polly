using System.ComponentModel.DataAnnotations;

namespace Polly.OrderedFallback;

/// <summary>
/// Represents the options for configuring an ordered fallback resilience strategy with a specific result type.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
public class OrderedFallbackStrategyOptions<TResult> : ResilienceStrategyOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderedFallbackStrategyOptions{TResult}"/> class.
    /// </summary>
    public OrderedFallbackStrategyOptions() => Name = OrderedFallbackConstants.DefaultName;

    /// <summary>
    /// Gets or sets a predicate that determines whether the fallback should be executed for a given outcome.
    /// </summary>
    /// <value>
    /// The default value is a predicate that falls back on any exception except <see cref="OperationCanceledException"/>. This property is required.
    /// </value>
    [Required]
    public Func<OrderedFallbackPredicateArguments<TResult>, ValueTask<bool>> ShouldHandle { get; set; } = DefaultPredicates<OrderedFallbackPredicateArguments<TResult>, TResult>.HandleOutcome;

    /// <summary>
    /// Gets the ordered list of fallback candidates to try when the primary operation fails.
    /// </summary>
    /// <value>
    /// The list of fallback candidates. The candidates are tried in the order they appear in the list.
    /// </value>
    public IList<OrderedFallbackCandidate<TResult>> Candidates { get; } = [];

    /// <summary>
    /// Gets or sets event delegate that is raised when a fallback candidate is about to be executed.
    /// </summary>
    /// <value>
    /// The default value is <see langword="null"/> instance.
    /// </value>
    public Func<OnOrderedFallbackArguments<TResult>, ValueTask>? OnOrderedFallback { get; set; }
}
