namespace Polly.Testing;

/// <summary>
/// Describes the category of the outcome associated with a <see cref="CollectedTelemetryEvent"/>.
/// </summary>
public enum TelemetryOutcomeCategory
{
    /// <summary>
    /// The event has no outcome associated with it.
    /// </summary>
    None = 0,

    /// <summary>
    /// The outcome represents a result.
    /// </summary>
    Result = 1,

    /// <summary>
    /// The outcome represents an exception.
    /// </summary>
    Exception = 2,
}
