namespace Polly.Testing;

/// <summary>
/// Determines which values are safe to copy into a telemetry snapshot.
/// </summary>
internal static class TelemetryValueSupport
{
    private static readonly HashSet<Type> SupportedScalars =
    [
        typeof(string),
        typeof(decimal),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(Guid),
        typeof(Uri),
    ];

    public static bool IsSupportedScalar(Type type) =>
        type.IsPrimitive || type.IsEnum || SupportedScalars.Contains(type);

    public static string GetTypeName(Type type) => type.FullName ?? type.Name;
}
