namespace Polly.KeyedCircuitBreaker;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Arguments used by the <see cref="KeyedCircuitBreakerStrategyOptions{TResult}.ShouldHandle"/> predicate.
/// </summary>
/// <typeparam name="TResult">The type of result.</typeparam>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct KeyedCircuitBreakerPredicateArguments<TResult> : IOutcomeArguments<TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerPredicateArguments{TResult}"/> struct.
    /// </summary>
    /// <param name="context">The context of the resilience operation.</param>
    /// <param name="outcome">The outcome of the resilience operation.</param>
    public KeyedCircuitBreakerPredicateArguments(ResilienceContext context, Outcome<TResult> outcome)
    {
        Context = context;
        Outcome = outcome;
    }

    /// <summary>
    /// Gets the outcome of the user-specified callback.
    /// </summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// Gets the context of the resilience operation.
    /// </summary>
    public ResilienceContext Context { get; }
}
