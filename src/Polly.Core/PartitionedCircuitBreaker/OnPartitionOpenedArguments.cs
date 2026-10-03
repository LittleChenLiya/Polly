namespace Polly.Partitioning;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Arguments used by <see cref="PartitionedCircuitBreakerStrategyOptions{TResult}.OnOpened"/> event.
/// </summary>
/// <typeparam name="TResult">The type of result.</typeparam>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct OnPartitionOpenedArguments<TResult> : IOutcomeArguments<TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OnPartitionOpenedArguments{TResult}"/> struct.
    /// </summary>
    /// <param name="context">The context in which the resilience operation occurred.</param>
    /// <param name="outcome">The outcome that caused the partition to open.</param>
    /// <param name="partitionKey">The key of the partition that opened.</param>
    /// <param name="breakDuration">The duration of the break.</param>
    public OnPartitionOpenedArguments(ResilienceContext context, Outcome<TResult> outcome, string partitionKey, TimeSpan breakDuration)
    {
        Context = context;
        Outcome = outcome;
        PartitionKey = partitionKey;
        BreakDuration = breakDuration;
    }

    /// <summary>
    /// Gets the outcome that caused the partition to open.
    /// </summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>
    /// Gets the context of this event.
    /// </summary>
    public ResilienceContext Context { get; }

    /// <summary>
    /// Gets the key of the partition that opened.
    /// </summary>
    public string PartitionKey { get; }

    /// <summary>
    /// Gets the duration of the break.
    /// </summary>
    public TimeSpan BreakDuration { get; }
}
