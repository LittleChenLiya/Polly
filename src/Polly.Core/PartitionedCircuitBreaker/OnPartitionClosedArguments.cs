namespace Polly.Partitioning;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Arguments used by <see cref="PartitionedCircuitBreakerStrategyOptions{TResult}.OnClosed"/> event.
/// </summary>
/// <typeparam name="TResult">The type of result.</typeparam>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct OnPartitionClosedArguments<TResult> : IOutcomeArguments<TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OnPartitionClosedArguments{TResult}"/> struct.
    /// </summary>
    /// <param name="context">The context in which the resilience operation occurred.</param>
    /// <param name="outcome">The outcome that caused the partition to reset.</param>
    /// <param name="partitionKey">The key of the partition that reset.</param>
    public OnPartitionClosedArguments(ResilienceContext context, Outcome<TResult> outcome, string partitionKey)
    {
        Context = context;
        Outcome = outcome;
        PartitionKey = partitionKey;
    }

    /// <summary>
    /// Gets the outcome that caused the partition to reset.
    /// </summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// Gets the context of this event.
    /// </summary>
    public ResilienceContext Context { get; }

    /// <summary>
    /// Gets the key of the partition that reset.
    /// </summary>
    public string PartitionKey { get; }
}
