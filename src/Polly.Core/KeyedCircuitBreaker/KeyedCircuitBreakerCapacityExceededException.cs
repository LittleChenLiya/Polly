#if !NETCOREAPP
using System.Runtime.Serialization;
#endif

namespace Polly.KeyedCircuitBreaker;

/// <summary>
/// Exception thrown when a keyed circuit breaker rejects an execution for a new key because
/// the maximum number of groups has been reached and no idle group (a group without executions
/// in flight) could be evicted to make room. A new group never preempts groups that have
/// executions in flight.
/// </summary>
#if !NETCOREAPP
[Serializable]
#endif
public class KeyedCircuitBreakerCapacityExceededException : ExecutionRejectedException
{
    internal const string DefaultMessage = "The keyed circuit breaker has reached its maximum number of groups and no idle group could be evicted.";

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerCapacityExceededException"/> class.
    /// </summary>
    public KeyedCircuitBreakerCapacityExceededException()
        : base(DefaultMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerCapacityExceededException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public KeyedCircuitBreakerCapacityExceededException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerCapacityExceededException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The inner exception.</param>
    public KeyedCircuitBreakerCapacityExceededException(string message, Exception inner)
        : base(message, inner)
    {
    }

    internal KeyedCircuitBreakerCapacityExceededException(string key, int maxGroups)
        : base($"The keyed circuit breaker has reached its maximum of {maxGroups} groups and all of them have executions in flight, " +
               $"so the new group for key '{key}' was rejected instead of preempting an active group.")
    {
        Key = key;
        MaxGroups = maxGroups;
    }

#pragma warning disable RS0016 // Add public types and members to the declared API
#if !NETCOREAPP
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerCapacityExceededException"/> class.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
    protected KeyedCircuitBreakerCapacityExceededException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Guard.NotNull(info);

        var enumerator = info.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            if (string.Equals(entry.Name, "MaxGroups", StringComparison.Ordinal))
            {
                MaxGroups = (int)entry.Value!;
            }
            else if (string.Equals(entry.Name, "Key", StringComparison.Ordinal))
            {
                Key = (string?)entry.Value;
            }
        }
    }

    /// <inheritdoc/>
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        Guard.NotNull(info);

        if (MaxGroups.HasValue)
        {
            info.AddValue("MaxGroups", MaxGroups.Value);
        }

        if (Key is not null)
        {
            info.AddValue("Key", Key);
        }

        base.GetObjectData(info, context);
    }
#endif
#pragma warning restore RS0016 // Add public types and members to the declared API

    /// <summary>
    /// Gets the key of the group that was rejected, if known.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// Gets the maximum number of groups that was reached, if known.
    /// </summary>
    public int? MaxGroups { get; }
}
