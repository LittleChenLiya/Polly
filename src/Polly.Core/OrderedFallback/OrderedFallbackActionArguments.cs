namespace Polly.OrderedFallback;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Arguments used by <see cref="OrderedFallbackCandidate{TResult}.Action"/>.
/// </summary>
/// <typeparam name="TResult">The type of result.</typeparam>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct OrderedFallbackActionArguments<TResult> : IOutcomeArguments<TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderedFallbackActionArguments{TResult}"/> struct.
    /// </summary>
    /// <param name="context">The context in which the resilience operation or event occurred.</param>
    /// <param name="outcome">The outcome of the resilience operation or event.</param>
    public OrderedFallbackActionArguments(ResilienceContext context, Outcome<TResult> outcome)
    {
        Context = context;
        Outcome = outcome;
    }

    /// <summary>
    /// Gets the outcome that should be handled by the fallback.
    /// </summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// Gets the context of this event.
    /// </summary>
    public ResilienceContext Context { get; }
}
