namespace Polly.Telemetry;

/// <summary>
/// Represents the category of the outcome associated with a resilience event.
/// </summary>
public enum ResultCategory
{
    /// <summary>
    /// The outcome is unknown (no outcome was provided).
    /// </summary>
    Unknown,

    /// <summary>
    /// The outcome represents a successful result.
    /// </summary>
    Success,

    /// <summary>
    /// The outcome represents an exception.
    /// </summary>
    Exception
}
