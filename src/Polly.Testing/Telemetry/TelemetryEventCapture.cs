using System.Collections.Concurrent;
using System.Reflection;
using Polly.Telemetry;

namespace Polly.Testing;

/// <summary>
/// Creates immutable snapshots of telemetry event arguments.
/// </summary>
internal static class TelemetryEventCapture
{
    private const string AttemptNumberProperty = "AttemptNumber";
    private const string VoidResultTypeName = "Polly.VoidResult";
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ReadableProperties = new();
    private static readonly IReadOnlyDictionary<string, CapturedValue> NoArguments = new Dictionary<string, CapturedValue>();

    public static CollectedTelemetryEvent Create<TResult, TArgs>(long sequence, in TelemetryEventArguments<TResult, TArgs> args)
    {
        var arguments = CaptureArguments(args.Arguments);
        var attempt = arguments.TryGetValue(AttemptNumberProperty, out var attemptValue) && attemptValue.Value is int attemptNumber
            ? attemptNumber
            : (int?)null;

        var outcome = TelemetryOutcomeCategory.None;
        string? exceptionType = null;
        var result = CapturedValue.Null;

        if (args.Outcome is { } outcomeValue)
        {
            if (outcomeValue.Exception is { } exception)
            {
                outcome = TelemetryOutcomeCategory.Exception;
                exceptionType = TelemetryValueSupport.GetTypeName(exception.GetType());
            }
            else
            {
                outcome = TelemetryOutcomeCategory.Result;
                var resultValue = outcomeValue.Result;
                result = resultValue is null || TelemetryValueSupport.GetTypeName(resultValue.GetType()) == VoidResultTypeName
                    ? CapturedValue.Null
                    : CapturedValue.Capture(resultValue);
            }
        }

        return new CollectedTelemetryEvent(
            sequence,
            args.Event.EventName ?? string.Empty,
            args.Event.Severity,
            args.Source?.PipelineName,
            args.Source?.PipelineInstanceName,
            args.Source?.StrategyName,
            attempt,
            outcome,
            exceptionType,
            result,
            arguments);
    }

    private static IReadOnlyDictionary<string, CapturedValue> CaptureArguments<TArgs>(TArgs args)
    {
        if (args is null)
        {
            return NoArguments;
        }

        var type = args.GetType();
        if (TelemetryValueSupport.IsSupportedScalar(type))
        {
            return new Dictionary<string, CapturedValue> { ["Value"] = CapturedValue.Capture(args) };
        }

        var properties = ReadableProperties.GetOrAdd(
            type,
            static t => t
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray());

        var snapshot = new Dictionary<string, CapturedValue>(properties.Length, StringComparer.Ordinal);
        foreach (var property in properties)
        {
            object? value;
            try
            {
                value = property.GetValue(args);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch
            {
                // A throwing property getter must not break the collection.
                snapshot[property.Name] = CapturedValue.FromType(property.PropertyType);
                continue;
            }
#pragma warning restore CA1031 // Do not catch general exception types

            snapshot[property.Name] = CapturedValue.Capture(value);
        }

        return snapshot;
    }
}
