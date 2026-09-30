namespace Polly.Retry;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Arguments that encapsulate the event raised when the retry loop stops because the total retry duration budget is exhausted.
/// </summary>
/// <remarks>
/// Always use the constructor when creating this struct, otherwise we do not guarantee binary compatibility.
/// </remarks>
public readonly struct RetryBudgetExceededArguments
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RetryBudgetExceededArguments"/> struct.
    /// </summary>
    /// <param name="attemptNumber">The attempt number after which the retry loop stopped.</param>
    /// <param name="elapsedTime">The total time elapsed since the start of the execution.</param>
    /// <param name="budget">The configured total retry duration budget.</param>
    public RetryBudgetExceededArguments(int attemptNumber, TimeSpan elapsedTime, TimeSpan budget)
    {
        AttemptNumber = attemptNumber;
        ElapsedTime = elapsedTime;
        Budget = budget;
    }

    /// <summary>
    /// Gets the attempt number after which the retry loop stopped.
    /// </summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// Gets the total time elapsed since the start of the execution.
    /// </summary>
    public TimeSpan ElapsedTime { get; }

    /// <summary>
    /// Gets the configured total retry duration budget.
    /// </summary>
    public TimeSpan Budget { get; }
}
