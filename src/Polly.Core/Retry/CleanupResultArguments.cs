namespace Polly.Retry;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Represents the arguments used by <see cref="RetryStrategyOptions{TResult}.CleanupResult"/> for cleaning up a result that is discarded before a retry.
/// </summary>
/// <typeparam name="TResult">The type of result.</typeparam>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct CleanupResultArguments<TResult> : IOutcomeArguments<TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupResultArguments{TResult}"/> struct.
    /// </summary>
    /// <param name="context">The context in which the resilience operation or event occurred.</param>
    /// <param name="outcome">The outcome of the user-callback whose result is about to be discarded.</param>
    /// <param name="attemptNumber">The zero-based attempt number that produced the discarded result.</param>
    public CleanupResultArguments(ResilienceContext context, Outcome<TResult> outcome, int attemptNumber)
    {
        Context = context;
        Outcome = outcome;
        AttemptNumber = attemptNumber;
    }

    /// <summary>
    /// Gets the outcome of the user-callback that produced the discarded result.
    /// </summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// Gets the context of this event.
    /// </summary>
    public ResilienceContext Context { get; }

    /// <summary>
    /// Gets the zero-based attempt number that produced the discarded result.
    /// </summary>
    public int AttemptNumber { get; }
}
