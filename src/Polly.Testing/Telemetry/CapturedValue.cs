using System.Globalization;

namespace Polly.Testing;

#pragma warning disable CA1815 // Override equals and operator equals on value types

/// <summary>
/// Represents a safely captured snapshot of a single value reported with a telemetry event.
/// </summary>
/// <remarks>
/// Only explicitly supported immutable scalar values are copied. Any other object is represented
/// by a type-name marker only: the original (potentially mutable) object is never stored and
/// <see cref="object.ToString"/> is never called on it.
/// </remarks>
public readonly struct CapturedValue
{
    private CapturedValue(CapturedValueKind kind, object? value, string? typeName)
    {
        Kind = kind;
        Value = value;
        TypeName = typeName;
    }

    /// <summary>
    /// Gets a captured value that represents <see langword="null"/>.
    /// </summary>
    public static CapturedValue Null { get; } = new(CapturedValueKind.Null, null, null);

    /// <summary>
    /// Gets a value indicating how the value was captured.
    /// </summary>
    public CapturedValueKind Kind { get; }

    /// <summary>
    /// Gets the copied scalar value when <see cref="Kind"/> is <see cref="CapturedValueKind.Scalar"/>; otherwise <see langword="null"/>.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the type name of the original object when <see cref="Kind"/> is <see cref="CapturedValueKind.TypeMarker"/>; otherwise <see langword="null"/>.
    /// </summary>
    public string? TypeName { get; }

    /// <summary>
    /// Captures the specified value in a safe manner.
    /// </summary>
    /// <param name="value">The value to capture.</param>
    /// <returns>A <see cref="CapturedValue"/> that never references the original object unless it is a supported immutable scalar.</returns>
    public static CapturedValue Capture(object? value)
    {
        if (value is null)
        {
            return Null;
        }

        var type = value.GetType();
        return TelemetryValueSupport.IsSupportedScalar(type)
            ? new CapturedValue(CapturedValueKind.Scalar, value, null)
            : FromType(type);
    }

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        CapturedValueKind.Null => "null",
        CapturedValueKind.Scalar => Value as string ?? Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => "<" + TypeName + ">",
    };

    internal static CapturedValue FromType(Type type) => new(CapturedValueKind.TypeMarker, null, TelemetryValueSupport.GetTypeName(type));
}
