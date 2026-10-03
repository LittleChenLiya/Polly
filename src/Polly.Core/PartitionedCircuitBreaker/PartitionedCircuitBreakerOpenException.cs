#if !NETCOREAPP
using System.Runtime.Serialization;
#endif

namespace Polly.Partitioning;

/// <summary>
/// Exception thrown when a partition of the partitioned circuit breaker is open.
/// </summary>
#if !NETCOREAPP
[Serializable]
#endif
public class PartitionedCircuitBreakerOpenException : ExecutionRejectedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerOpenException"/> class.
    /// </summary>
    public PartitionedCircuitBreakerOpenException()
        : base(PartitionedCircuitBreakerConstants.DefaultOpenMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerOpenException"/> class.
    /// </summary>
    /// <param name="retryAfter">The period after which the partition will close.</param>
    public PartitionedCircuitBreakerOpenException(TimeSpan retryAfter)
        : base($"{PartitionedCircuitBreakerConstants.DefaultOpenMessage} It can be retried after '{retryAfter}'.")
        => RetryAfter = retryAfter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerOpenException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PartitionedCircuitBreakerOpenException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerOpenException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="retryAfter">The period after which the partition will close.</param>
    public PartitionedCircuitBreakerOpenException(string message, TimeSpan retryAfter)
        : base(message) => RetryAfter = retryAfter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerOpenException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The inner exception.</param>
    public PartitionedCircuitBreakerOpenException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerOpenException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="retryAfter">The period after which the partition will close.</param>
    /// <param name="inner">The inner exception.</param>
    public PartitionedCircuitBreakerOpenException(string message, TimeSpan retryAfter, Exception inner)
        : base(message, inner) => RetryAfter = retryAfter;

#pragma warning disable RS0016 // Add public types and members to the declared API
#if !NETCOREAPP
    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerOpenException"/> class.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
    protected PartitionedCircuitBreakerOpenException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Guard.NotNull(info);

        // https://github.com/dotnet/runtime/issues/42460
        SerializationInfoEnumerator enumerator = info.GetEnumerator();
        while (enumerator.MoveNext())
        {
            SerializationEntry entry = enumerator.Current;
            if (string.Equals(entry.Name, "RetryAfter", StringComparison.Ordinal))
            {
                var ticks = (long)entry.Value;
                RetryAfter = new TimeSpan(ticks);
                break;
            }
        }
    }

    /// <inheritdoc/>
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        Guard.NotNull(info);
        if (RetryAfter.HasValue)
        {
            info.AddValue("RetryAfter", RetryAfter.Value.Ticks);
        }

        base.GetObjectData(info, context);
    }
#endif
#pragma warning restore RS0016 // Add public types and members to the declared API

    /// <summary>
    /// Gets the amount of time before the partition becomes closed, if known.
    /// </summary>
    /// <remarks>
    /// This value is specified when the instance is constructed and may be inaccurate if consumed at a later time.
    /// </remarks>
    public TimeSpan? RetryAfter { get; }
}
