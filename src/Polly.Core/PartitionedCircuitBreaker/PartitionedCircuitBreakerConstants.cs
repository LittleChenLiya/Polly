namespace Polly.Partitioning;

internal static class PartitionedCircuitBreakerConstants
{
    public const string DefaultName = "PartitionedCircuitBreaker";

    public const string OnPartitionOpened = "OnPartitionOpened";

    public const string OnPartitionClosed = "OnPartitionClosed";

    public const int DefaultFailureThreshold = 5;

    public const int DefaultMaxPartitionCount = 100;

    public static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(5);

    public const string DefaultOpenMessage = "The partitioned circuit breaker for the specified key is now open and is not allowing calls.";

    public const string DefaultRejectedMessage = "The partitioned circuit breaker has reached its limit and a new partition cannot be created because no idle partition can be evicted.";
}
