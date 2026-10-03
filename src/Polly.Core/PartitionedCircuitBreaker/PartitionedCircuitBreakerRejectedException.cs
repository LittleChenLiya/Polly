#if !NETCOREAPP
using System.Runtime.Serialization;
#endif

namespace Polly.Partitioning;

/// <summary>
/// Exception thrown when a new partition cannot be created because the partition count reached its limit
/// and no idle partition can be evicted.
/// </summary>
#if !NETCOREAPP
[Serializable]
#endif
public class PartitionedCircuitBreakerRejectedException : ExecutionRejectedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerRejectedException"/> class.
    /// </summary>
    public PartitionedCircuitBreakerRejectedException()
        : base(PartitionedCircuitBreakerConstants.DefaultRejectedMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerRejectedException"/> class.
    /// </summary>
    /// <param name="maxPartitionCount">The maximum number of partitions the strategy can track.</param>
    public PartitionedCircuitBreakerRejectedException(int maxPartitionCount)
        : base($"{PartitionedCircuitBreakerConstants.DefaultRejectedMessage} The limit is '{maxPartitionCount}' partitions.")
        => MaxPartitionCount = maxPartitionCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerRejectedException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PartitionedCircuitBreakerRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerRejectedException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The inner exception.</param>
    public PartitionedCircuitBreakerRejectedException(string message, Exception inner)
        : base(message, inner)
    {
    }

#pragma warning disable RS0016 // Add public types and members to the declared API
#if !NETCOREAPP
    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCircuitBreakerRejectedException"/> class.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
    protected PartitionedCircuitBreakerRejectedException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Guard.NotNull(info);

        SerializationInfoEnumerator enumerator = info.GetEnumerator();
        while (enumerator.MoveNext())
        {
            SerializationEntry entry = enumerator.Current;
            if (string.Equals(entry.Name, "MaxPartitionCount", StringComparison.Ordinal))
            {
                MaxPartitionCount = (int)entry.Value;
                break;
            }
        }
    }

    /// <inheritdoc/>
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        Guard.NotNull(info);
        info.AddValue("MaxPartitionCount", MaxPartitionCount);

        base.GetObjectData(info, context);
    }
#endif
#pragma warning restore RS0016 // Add public types and members to the declared API

    /// <summary>
    /// Gets the maximum number of partitions the strategy can track.
    /// </summary>
    /// <remarks>
    /// The value is <see langword="null"/> if it was not provided when the instance was constructed.
    /// </remarks>
    public int? MaxPartitionCount { get; }
}
