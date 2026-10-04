namespace Polly.Telemetry;

/// <summary>
/// Represents an immutable, sealed snapshot of the events collected for a single call.
/// </summary>
public sealed class EventSnapshot
{
    private readonly object _sync = new();
    private readonly List<EventRecord> _events = [];
    private readonly int _maxEvents;
    private int _totalCount;
    private int _droppedCount;
    private bool _isSealed;

    internal EventSnapshot(int callIndex, int maxEvents)
    {
        CallIndex = callIndex;
        _maxEvents = maxEvents;
    }

    /// <summary>
    /// Gets the zero-based index of the call within the collector.
    /// </summary>
    public int CallIndex { get; }

    /// <summary>
    /// Gets a value indicating whether the snapshot has been sealed.
    /// </summary>
    public bool IsSealed
    {
        get
        {
            lock (_sync)
            {
                return _isSealed;
            }
        }
    }

    /// <summary>
    /// Gets the total number of events seen for the call, including dropped events.
    /// </summary>
    public int TotalCount
    {
        get
        {
            lock (_sync)
            {
                return _totalCount;
            }
        }
    }

    /// <summary>
    /// Gets the number of events stored in the snapshot.
    /// </summary>
    public int CollectedCount
    {
        get
        {
            lock (_sync)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of events that were dropped because the snapshot reached its limit.
    /// </summary>
    public int DroppedCount
    {
        get
        {
            lock (_sync)
            {
                return _droppedCount;
            }
        }
    }

    /// <summary>
    /// Gets the collected events in their original order.
    /// </summary>
#pragma warning disable S2365 // Properties should not copy collections
    public IReadOnlyList<EventRecord> Events
    {
        get
        {
            lock (_sync)
            {
                return _events.ToList();
            }
        }
    }
#pragma warning restore S2365 // Properties should not copy collections

    internal void Add<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        EventRecord? record = null;

        lock (_sync)
        {
            if (_isSealed)
            {
                return;
            }

            _totalCount++;

            if (_events.Count >= _maxEvents)
            {
                _droppedCount++;
                return;
            }

            record = EventRecordFactory.Create(in args, _totalCount - 1);
        }

        if (record is not null)
        {
            lock (_sync)
            {
                _events.Add(record);
            }
        }
    }

    internal void Seal()
    {
        lock (_sync)
        {
            _isSealed = true;
        }
    }
}
