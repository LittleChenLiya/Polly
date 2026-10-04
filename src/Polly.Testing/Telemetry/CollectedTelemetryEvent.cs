using Polly.Telemetry;

namespace Polly.Testing;

/// <summary>
/// An immutable snapshot of a single telemetry event that was collected during a pipeline invocation.
/// </summary>
/// <remarks>
/// The snapshot preserves the event name, the order in which events were reported, the attempt number
/// (when the event arguments expose one) and the category of the outcome. Only explicitly supported
/// scalar values are copied; any other object is represented by a type-name marker.
/// </remarks>
public sealed class CollectedTelemetryEvent
{
    internal CollectedTelemetryEvent(
        long sequence,
        string eventName,
        ResilienceEventSeverity severity,
        string? pipelineName,
        string? pipelineInstanceName,
        string? strategyName,
        int? attempt,
        TelemetryOutcomeCategory outcome,
        string? exceptionType,
        CapturedValue result,
        IReadOnlyDictionary<string, CapturedValue> arguments)
    {
        Sequence = sequence;
        EventName = eventName;
        Severity = severity;
        PipelineName = pipelineName;
        PipelineInstanceName = pipelineInstanceName;
        StrategyName = strategyName;
        Attempt = attempt;
        Outcome = outcome;
        ExceptionType = exceptionType;
        Result = result;
        Arguments = arguments;
    }

    /// <summary>
    /// Gets the zero-based sequence number of the event within the invocation, preserving the reporting order.
    /// </summary>
    public long Sequence { get; }

    /// <summary>
    /// Gets the name of the reported event, for example <c>OnRetry</c> or <c>ExecutionAttempt</c>.
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// Gets the severity of the reported event.
    /// </summary>
    public ResilienceEventSeverity Severity { get; }

    /// <summary>
    /// Gets the name of the pipeline that reported the event, if any.
    /// </summary>
    public string? PipelineName { get; }

    /// <summary>
    /// Gets the instance name of the pipeline that reported the event, if any.
    /// </summary>
    public string? PipelineInstanceName { get; }

    /// <summary>
    /// Gets the name of the strategy that reported the event, if any.
    /// </summary>
    public string? StrategyName { get; }

    /// <summary>
    /// Gets the zero-based attempt number associated with the event, if the event arguments exposed one.
    /// </summary>
    public int? Attempt { get; }

    /// <summary>
    /// Gets the category of the outcome associated with the event.
    /// </summary>
    public TelemetryOutcomeCategory Outcome { get; }

    /// <summary>
    /// Gets the full type name of the exception when <see cref="Outcome"/> is <see cref="TelemetryOutcomeCategory.Exception"/>; otherwise <see langword="null"/>.
    /// </summary>
    public string? ExceptionType { get; }

    /// <summary>
    /// Gets the safely captured result value when <see cref="Outcome"/> is <see cref="TelemetryOutcomeCategory.Result"/>.
    /// </summary>
    public CapturedValue Result { get; }

    /// <summary>
    /// Gets the safely captured snapshot of the event arguments.
    /// </summary>
    /// <remarks>
    /// Only explicitly supported scalar property values are copied. Any other property value is
    /// represented by a type-name marker and the original arguments object is never stored.
    /// </remarks>
    public IReadOnlyDictionary<string, CapturedValue> Arguments { get; }
}
