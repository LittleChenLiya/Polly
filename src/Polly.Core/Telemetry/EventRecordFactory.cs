using System.Reflection;

namespace Polly.Telemetry;

internal static class EventRecordFactory
{
    private const string AttemptNumberPropertyName = "AttemptNumber";
    private const string HandledPropertyName = "Handled";
    private const string DurationPropertyName = "Duration";
    private const string RetryDelayPropertyName = "RetryDelay";

#pragma warning disable IDE0028 // Simplify collection initialization
    private static readonly HashSet<Type> SupportedScalars = new()
    {
        typeof(bool),
        typeof(byte),
        typeof(char),
        typeof(decimal),
        typeof(double),
        typeof(float),
        typeof(int),
        typeof(long),
        typeof(sbyte),
        typeof(short),
        typeof(uint),
        typeof(ulong),
        typeof(ushort),
        typeof(string),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(Guid)
    };

#pragma warning restore IDE0028 // Simplify collection initialization
    public static EventRecord Create<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args, int sequence)
    {
        var source = args.Source;
        var ev = args.Event;
        var context = args.Context;
        var outcome = args.Outcome;
        var arguments = args.Arguments;

        int? attemptNumber = null;
        bool? handled = null;
        TimeSpan? duration = null;
        TimeSpan? retryDelay = null;
        var fields = new Dictionary<string, object?>();
        var unknownFieldTypes = new Dictionary<string, string>();

        if (arguments is not null)
        {
            foreach (var property in arguments.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(arguments);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception)
                {
                    // Ignore properties that throw on access.
                    continue;
                }
#pragma warning restore CA1031 // Do not catch general exception types

                if (value is null)
                {
                    continue;
                }

                var propertyName = property.Name;
                var propertyType = property.PropertyType;

                if (propertyName == AttemptNumberPropertyName && propertyType == typeof(int))
                {
                    attemptNumber = (int)value;
                }
                else if (propertyName == HandledPropertyName && propertyType == typeof(bool))
                {
                    handled = (bool)value;
                }
                else if (propertyName == DurationPropertyName && propertyType == typeof(TimeSpan))
                {
                    duration = (TimeSpan)value;
                }
                else if (propertyName == RetryDelayPropertyName && propertyType == typeof(TimeSpan))
                {
                    retryDelay = (TimeSpan)value;
                }

                if (IsSupportedScalar(propertyType))
                {
                    fields[propertyName] = value;
                }
                else
                {
                    unknownFieldTypes[propertyName] = propertyType.FullName ?? propertyType.Name;
                }
            }
        }

        var resultCategory = ResultCategory.Unknown;
        string? exceptionType = null;

        if (outcome is not null)
        {
            if (outcome.Value.Exception is not null)
            {
                resultCategory = ResultCategory.Exception;
                exceptionType = outcome.Value.Exception.GetType().FullName;
            }
            else
            {
                resultCategory = ResultCategory.Success;
            }
        }

        return new EventRecord(
            ev.EventName,
            sequence,
            ev.Severity,
            source.PipelineName,
            source.PipelineInstanceName,
            source.StrategyName,
            context?.OperationKey,
            attemptNumber,
            handled,
            duration,
            retryDelay,
            resultCategory,
            exceptionType,
            fields,
            unknownFieldTypes);
    }

    private static bool IsSupportedScalar(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            return true;
        }

        return SupportedScalars.Contains(underlyingType);
    }
}
