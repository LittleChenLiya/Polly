using Polly.Telemetry;

namespace Polly.Testing;

/// <summary>
/// Represents a single invocation during which telemetry events are collected.
/// </summary>
/// <remarks>
/// Create an invocation with <see cref="TelemetryEventCollector.BeginInvocation"/> and dispose it when
/// the invocation finishes. Disposing seals the collected events into an immutable
/// <see cref="TelemetryInvocationSnapshot"/>. Each invocation collects its own isolated set of events,
/// even when multiple invocations run concurrently.
/// </remarks>
public sealed class TelemetryInvocation : IDisposable
{
    private readonly TelemetryEventCollector _collector;
    private readonly List<CollectedTelemetryEvent> _events = [];
    private readonly object _sync = new();
    private TelemetryInvocationSnapshot? _snapshot;
    private long _droppedEvents;

    internal TelemetryInvocation(TelemetryEventCollector collector, TelemetryInvocation? previous)
    {
        _collector = collector;
        Previous = previous;
    }

    /// <summary>
    /// Gets a value indicating whether this invocation has been sealed and its snapshot is available.
    /// </summary>
    public bool IsSealed
    {
        get
        {
            lock (_sync)
            {
                return _snapshot is not null;
            }
        }
    }

    /// <summary>
    /// Gets the number of events that were dropped because the per-invocation limit was reached.
    /// </summary>
    public long DroppedEvents
    {
        get
        {
            lock (_sync)
            {
                return _droppedEvents;
            }
        }
    }

    /// <summary>
    /// Gets the sealed snapshot of the collected events.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the invocation has not been disposed yet.</exception>
    public TelemetryInvocationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot ?? throw new InvalidOperationException(
                    "The invocation is still active. Dispose it to seal the collected events into a snapshot before accessing them.");
            }
        }
    }

    internal TelemetryInvocation? Previous { get; }

    /// <summary>
    /// Seals the collected events into an immutable <see cref="TelemetryInvocationSnapshot"/> and detaches
    /// this invocation from the collector. Subsequent calls have no effect.
    /// </summary>
    public void Dispose()
    {
        TelemetryInvocationSnapshot snapshot;
        lock (_sync)
        {
            if (_snapshot is not null)
            {
                return;
            }

            _snapshot = snapshot = new TelemetryInvocationSnapshot(_events.ToArray(), _droppedEvents);
        }

        _collector.Seal(this, snapshot);
    }

    internal void Capture<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        lock (_sync)
        {
            if (_snapshot is not null)
            {
                // Late events reported after the invocation was sealed are ignored.
                return;
            }

            if (_events.Count >= _collector.MaxEventsPerInvocation)
            {
                // The limit is reached: only count the event, never materialize or store it.
                _droppedEvents++;
                return;
            }

            _events.Add(TelemetryEventCapture.Create(_events.Count, in args));
        }
    }
}
