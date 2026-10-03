namespace Polly.Partitioning;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Arguments used by <see cref="PartitionedCircuitBreakerStrategyOptions{TResult}.ShouldHandle"/> predicate.
/// </summary>
/// <typeparam name="TResult">The type of result.</typeparam>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct PartitionedCircuitBreakerPredicateArguments<TResult> : IOutcomeArguments<TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerPredicateArguments{TResult}"/> struct.
    /// </summary>
    /// <param name="context">The context in which the resilience operation occurred.</param>
    /// <param name="outcome">The outcome of the resilience operation.</param>
    public PartitionedCircuitBreakerPredicateArguments(ResilienceContext context, Outcome<TResult> outcome)
    {
        Context = context;
        Outcome = outcome;
    }

    /// <summary>
    /// Gets the outcome of the user-specified callback.
    /// </summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// Gets the context of this event.
    /// </summary>
    public ResilienceContext Context { get; }
}
