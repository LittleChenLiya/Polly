namespace Polly.KeyedCircuitBreaker;

internal static class KeyedCircuitBreakerConstants
{
    public const string DefaultName = "KeyedCircuitBreaker";

    public const int DefaultFailureThreshold = 5;

    public const int DefaultMaxGroups = 1024;

    public static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(5);
}
