namespace Polly.Testing;

/// <summary>
/// An immutable, sealed snapshot of the telemetry events collected during a single pipeline invocation.
/// </summary>
public sealed class TelemetryInvocationSnapshot
{
    internal TelemetryInvocationSnapshot(IReadOnlyList<CollectedTelemetryEvent> events, long droppedEvents)
    {
        Events = events;
        DroppedEvents = droppedEvents;
    }

    /// <summary>
    /// Gets the collected events in the order in which they were reported.
    /// </summary>
    public IReadOnlyList<CollectedTelemetryEvent> Events { get; }

    /// <summary>
    /// Gets the number of events that were dropped because the per-invocation limit was reached.
    /// </summary>
    /// <remarks>
    /// Dropped events are only counted; they are never materialized or stored.
    /// </remarks>
    public long DroppedEvents { get; }
}
