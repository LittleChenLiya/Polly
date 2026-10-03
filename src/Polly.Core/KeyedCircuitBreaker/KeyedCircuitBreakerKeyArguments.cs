namespace Polly.KeyedCircuitBreaker;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Arguments used by the <see cref="KeyedCircuitBreakerStrategyOptions{TResult}.KeyFactory"/> delegate.
/// </summary>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct KeyedCircuitBreakerKeyArguments
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerKeyArguments"/> struct.
    /// </summary>
    /// <param name="context">The context of the resilience operation.</param>
    public KeyedCircuitBreakerKeyArguments(ResilienceContext context) => Context = context;

    /// <summary>
    /// Gets the context of the resilience operation.
    /// </summary>
    /// <remarks>
    /// Use <see cref="ResilienceContext.Properties"/> to flow the caller-provided key (for example a tenant identifier)
    /// to the key factory.
    /// </remarks>
    public ResilienceContext Context { get; }
}
