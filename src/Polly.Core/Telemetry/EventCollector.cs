namespace Polly.Telemetry;

/// <summary>
/// A telemetry listener that collects resilience events into per-call snapshots for later analysis.
/// </summary>
/// <remarks>
/// <para>
/// The collector does not replace any existing telemetry callback. It is an additional <see cref="TelemetryListener"/>
/// that can be registered alongside other listeners via <c>TelemetryOptions.TelemetryListeners</c>.
/// </para>
/// <para>
/// Events are grouped by call. A new snapshot is started when a <c>PipelineExecuting</c> event is received and
/// sealed when the corresponding <c>PipelineExecuted</c> event is received. Each snapshot preserves the event names,
/// order, attempt numbers, and result categories. Only explicitly supported scalar fields are copied from the event
/// arguments; unknown objects are marked by their type name and are never stored or converted to strings.
/// </para>
/// <para>
/// The collector is thread-safe. If the number of events for a call exceeds the configured limit, additional events
/// are dropped and the <see cref="EventSnapshot.DroppedCount"/> is incremented. Errors raised by the collector are
/// swallowed so they cannot affect the business result.
/// </para>
/// </remarks>
public sealed class EventCollector : TelemetryListener, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<ResilienceContext, EventSnapshot> _activeSnapshots = [];
    private readonly List<EventSnapshot> _snapshots = [];
    private int _nextCallIndex;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventCollector"/> class.
    /// </summary>
    /// <param name="maxEventsPerCall">The maximum number of events to collect per call. Defaults to 1024.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxEventsPerCall"/> is less than or equal to zero.</exception>
    public EventCollector(int maxEventsPerCall = 1024)
    {
        if (maxEventsPerCall <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEventsPerCall), "The maximum number of events per call must be greater than zero.");
        }

        MaxEventsPerCall = maxEventsPerCall;
    }

    /// <summary>
    /// Gets the maximum number of events that the collector stores per call.
    /// </summary>
    public int MaxEventsPerCall { get; }

    /// <summary>
    /// Gets the number of calls that have been observed by the collector.
    /// </summary>
    public int CallCount
    {
        get
        {
            lock (_sync)
            {
                return _snapshots.Count;
            }
        }
    }

    /// <summary>
    /// Gets the snapshots for all observed calls, in call order.
    /// </summary>
    /// <returns>A list of all collected snapshots.</returns>
#pragma warning disable S4049, CA1024 // Consider making method a property
    public IReadOnlyList<EventSnapshot> GetSnapshots()
#pragma warning restore S4049, CA1024 // Consider making method a property
    {
        lock (_sync)
        {
            return _snapshots.ToList();
        }
    }

    /// <summary>
    /// Gets the snapshot for the call at the specified index.
    /// </summary>
    /// <param name="callIndex">The zero-based index of the call.</param>
    /// <returns>The snapshot, or <see langword="null"/> if the index is out of range.</returns>
    public EventSnapshot? GetSnapshot(int callIndex)
    {
        lock (_sync)
        {
            if (callIndex < 0 || callIndex >= _snapshots.Count)
            {
                return null;
            }

            return _snapshots[callIndex];
        }
    }

    /// <summary>
    /// Gets the last observed call snapshot.
    /// </summary>
    /// <returns>The last snapshot, or <see langword="null"/> if no calls have been observed.</returns>
#pragma warning disable S4049, CA1024 // Consider making method a property
    public EventSnapshot? GetLastSnapshot()
#pragma warning restore S4049, CA1024 // Consider making method a property
    {
        lock (_sync)
        {
            if (_snapshots.Count == 0)
            {
                return null;
            }

            return _snapshots[_snapshots.Count - 1];
        }
    }

    /// <summary>
    /// Seals all active snapshots so that no further events can be added to them.
    /// </summary>
    public void Seal()
    {
        lock (_sync)
        {
            foreach (var snapshot in _activeSnapshots.Values)
            {
                snapshot.Seal();
            }

            _activeSnapshots.Clear();
        }
    }

    /// <inheritdoc/>
    public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        // Collector errors must not change business results.
#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            WriteCore(in args);
        }
        catch (Exception)
        {
            // Swallow all exceptions to ensure the collector does not affect business results.
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var snapshot in _activeSnapshots.Values)
            {
                snapshot.Seal();
            }

            _activeSnapshots.Clear();
        }
    }

    private void WriteCore<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        var context = args.Context;

        if (context is null)
        {
            return;
        }

        var eventName = args.Event.EventName;

        if (string.Equals(eventName, TelemetryUtil.PipelineExecuting, StringComparison.Ordinal))
        {
            StartSnapshot(context);
            AddEvent(context, in args);
            return;
        }

        if (string.Equals(eventName, TelemetryUtil.PipelineExecuted, StringComparison.Ordinal))
        {
            AddEvent(context, in args);
            SealSnapshot(context);
            return;
        }

        AddEvent(context, in args);
    }

    private void StartSnapshot(ResilienceContext context)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            // If a snapshot for this context is already active (e.g. PipelineExecuted was missed),
            // seal it before starting a new one.
            if (_activeSnapshots.TryGetValue(context, out var existing))
            {
                existing.Seal();
            }

            var snapshot = new EventSnapshot(_nextCallIndex++, MaxEventsPerCall);
            _snapshots.Add(snapshot);
            _activeSnapshots[context] = snapshot;
        }
    }

    private void SealSnapshot(ResilienceContext context)
    {
        lock (_sync)
        {
            if (_activeSnapshots.TryGetValue(context, out var snapshot))
            {
                snapshot.Seal();
                _activeSnapshots.Remove(context);
            }
        }
    }

    private void AddEvent<TResult, TArgs>(ResilienceContext context, in TelemetryEventArguments<TResult, TArgs> args)
    {
        EventSnapshot? snapshot = null;

        lock (_sync)
        {
            if (_activeSnapshots.TryGetValue(context, out var active))
            {
                snapshot = active;
            }
        }

        snapshot?.Add(in args);
    }
}
