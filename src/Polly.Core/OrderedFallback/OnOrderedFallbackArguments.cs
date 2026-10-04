namespace Polly.OrderedFallback;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Represents arguments used in ordered fallback handling scenarios.
/// </summary>
/// <typeparam name="TResult">The type of result.</typeparam>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct OnOrderedFallbackArguments<TResult> : IOutcomeArguments<TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OnOrderedFallbackArguments{TResult}"/> struct.
    /// </summary>
    /// <param name="context">The context in which the resilience operation or event occurred.</param>
    /// <param name="outcome">The outcome of the resilience operation or event.</param>
    /// <param name="candidateName">The name of the fallback candidate that is about to be executed.</param>
    public OnOrderedFallbackArguments(ResilienceContext context, Outcome<TResult> outcome, string? candidateName)
    {
        Outcome = outcome;
        Context = context;
        CandidateName = candidateName;
    }

    /// <summary>
    /// Gets the outcome that caused the fallback to be executed.
    /// </summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// Gets the context of this event.
    /// </summary>
    public ResilienceContext Context { get; }

    /// <summary>
    /// Gets the name of the fallback candidate that is about to be executed.
    /// </summary>
    public string? CandidateName { get; }
}
