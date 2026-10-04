using Polly.Telemetry;

namespace Polly.Testing;

/// <summary>
/// A <see cref="TelemetryListener"/> that explicitly collects the telemetry events of individual
/// pipeline invocations so they can be analyzed in tests.
/// </summary>
/// <remarks>
/// <para>
/// The collector composes with the existing telemetry instead of replacing it: pass the listener that
/// is already in use to the constructor and it keeps receiving every event.
/// </para>
/// <para>
/// Use <see cref="BeginInvocation"/> to collect the events of a single invocation. Events reported
/// outside of an invocation are not collected. The collector never throws: any error raised while
/// collecting or while forwarding to the inner listener is swallowed so the outcome of the pipeline
/// under test is never affected.
/// </para>
/// </remarks>
public sealed class TelemetryEventCollector : TelemetryListener
{
    /// <summary>
    /// The default maximum number of events collected per invocation.
    /// </summary>
    public const int DefaultMaxEventsPerInvocation = 1024;

    private readonly TelemetryListener? _innerListener;
    private readonly AsyncLocal<TelemetryInvocation?> _current = new();
    private readonly List<TelemetryInvocationSnapshot> _snapshots = [];
    private readonly object _sync = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryEventCollector"/> class.
    /// </summary>
    /// <param name="innerListener">The existing telemetry listener that keeps receiving every event, if any.</param>
    /// <param name="maxEventsPerInvocation">The maximum number of events collected per invocation. Events beyond this limit are counted as dropped and are not stored.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxEventsPerInvocation"/> is less than 1.</exception>
    public TelemetryEventCollector(TelemetryListener? innerListener = null, int maxEventsPerInvocation = DefaultMaxEventsPerInvocation)
    {
        if (maxEventsPerInvocation < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEventsPerInvocation), maxEventsPerInvocation, "The maximum number of events per invocation must be at least 1.");
        }

        _innerListener = innerListener;
        MaxEventsPerInvocation = maxEventsPerInvocation;
    }

    /// <summary>
    /// Gets the maximum number of events collected per invocation.
    /// </summary>
    public int MaxEventsPerInvocation { get; }

    /// <summary>
    /// Begins collecting the telemetry events of a single invocation.
    /// </summary>
    /// <returns>A <see cref="TelemetryInvocation"/> that collects events until it is disposed.</returns>
    /// <remarks>
    /// The invocation is bound to the current asynchronous control flow, so concurrent invocations
    /// collect isolated sets of events. Dispose the returned invocation to seal the collected events
    /// into an immutable snapshot.
    /// </remarks>
    public TelemetryInvocation BeginInvocation()
    {
        var invocation = new TelemetryInvocation(this, _current.Value);
        _current.Value = invocation;
        return invocation;
    }

    /// <summary>
    /// Gets the sealed snapshots of all completed invocations, in the order in which they were sealed.
    /// </summary>
    /// <returns>A snapshot of the sealed invocations.</returns>
#pragma warning disable S4049 // Properties should be preferred over methods. This is a method because each call returns a new copy.
    public IReadOnlyList<TelemetryInvocationSnapshot> GetSnapshots()
#pragma warning restore S4049
    {
        lock (_sync)
        {
            return _snapshots.ToArray();
        }
    }

    /// <inheritdoc/>
    public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        // The collector must never change the outcome of the pipeline under test,
        // so any error raised here is swallowed.
        try
        {
            _innerListener?.Write(in args);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
        {
            // The inner listener failed, keep collecting.
        }
#pragma warning restore CA1031 // Do not catch general exception types

        var invocation = _current.Value;
        if (invocation is null)
        {
            return;
        }

        try
        {
            invocation.Capture(in args);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
        {
            // Capturing failed, the pipeline outcome is not affected.
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    internal void Seal(TelemetryInvocation invocation, TelemetryInvocationSnapshot snapshot)
    {
        if (ReferenceEquals(_current.Value, invocation))
        {
            _current.Value = invocation.Previous;
        }

        lock (_sync)
        {
            _snapshots.Add(snapshot);
        }
    }
}
