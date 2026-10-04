namespace Polly.Telemetry;

/// <summary>
/// Represents an immutable snapshot of a single collected resilience event.
/// </summary>
/// <remarks>
/// Only scalar fields are copied from the original event arguments. Unknown objects are marked
/// by their type name and are never stored or converted to strings.
/// </remarks>
public sealed class EventRecord
{
#pragma warning disable S107 // Constructor has too many parameters
    internal EventRecord(
        string eventName,
        int sequence,
        ResilienceEventSeverity severity,
        string? pipelineName,
        string? pipelineInstanceName,
        string? strategyName,
        string? operationKey,
        int? attemptNumber,
        bool? handled,
        TimeSpan? duration,
        TimeSpan? retryDelay,
        ResultCategory resultCategory,
        string? exceptionType,
        IReadOnlyDictionary<string, object?> fields,
        IReadOnlyDictionary<string, string> unknownFieldTypes)
#pragma warning restore S107 // Constructor has too many parameters
    {
        EventName = eventName;
        Sequence = sequence;
        Severity = severity;
        PipelineName = pipelineName;
        PipelineInstanceName = pipelineInstanceName;
        StrategyName = strategyName;
        OperationKey = operationKey;
        AttemptNumber = attemptNumber;
        Handled = handled;
        Duration = duration;
        RetryDelay = retryDelay;
        ResultCategory = resultCategory;
        ExceptionType = exceptionType;
        Fields = fields;
        UnknownFieldTypes = unknownFieldTypes;
    }

    /// <summary>
    /// Gets the name of the resilience event.
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// Gets the zero-based sequence number of the event within its call.
    /// </summary>
    public int Sequence { get; }

    /// <summary>
    /// Gets the severity of the resilience event.
    /// </summary>
    public ResilienceEventSeverity Severity { get; }

    /// <summary>
    /// Gets the name of the pipeline that produced the event.
    /// </summary>
    public string? PipelineName { get; }

    /// <summary>
    /// Gets the instance name of the pipeline that produced the event.
    /// </summary>
    public string? PipelineInstanceName { get; }

    /// <summary>
    /// Gets the name of the strategy that produced the event.
    /// </summary>
    public string? StrategyName { get; }

    /// <summary>
    /// Gets the operation key associated with the event.
    /// </summary>
    public string? OperationKey { get; }

    /// <summary>
    /// Gets the zero-based attempt number, if the event carries one.
    /// </summary>
    public int? AttemptNumber { get; }

    /// <summary>
    /// Gets a value indicating whether the attempt was handled by the strategy, if the event carries this information.
    /// </summary>
    public bool? Handled { get; }

    /// <summary>
    /// Gets the duration of the attempt or execution, if the event carries one.
    /// </summary>
    public TimeSpan? Duration { get; }

    /// <summary>
    /// Gets the retry delay before the next attempt, if the event carries one.
    /// </summary>
    public TimeSpan? RetryDelay { get; }

    /// <summary>
    /// Gets the category of the outcome associated with the event.
    /// </summary>
    public ResultCategory ResultCategory { get; }

    /// <summary>
    /// Gets the full type name of the exception, if the outcome is an exception.
    /// </summary>
    public string? ExceptionType { get; }

    /// <summary>
    /// Gets the scalar fields copied from the event arguments.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Fields { get; }

    /// <summary>
    /// Gets the type names of fields that could not be copied because they are not supported scalars.
    /// </summary>
    public IReadOnlyDictionary<string, string> UnknownFieldTypes { get; }
}
