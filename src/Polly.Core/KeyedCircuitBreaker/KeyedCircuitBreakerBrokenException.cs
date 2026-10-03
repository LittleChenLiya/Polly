#if !NETCOREAPP
using System.Runtime.Serialization;
#endif

namespace Polly.KeyedCircuitBreaker;

/// <summary>
/// Exception thrown when a keyed circuit breaker rejects an execution because the circuit
/// for the resolved key is broken and its cool-down has not elapsed yet.
/// </summary>
#if !NETCOREAPP
[Serializable]
#endif
public class KeyedCircuitBreakerBrokenException : ExecutionRejectedException
{
    internal const string DefaultMessage = "The circuit for the resolved key is now open and is not allowing calls.";

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerBrokenException"/> class.
    /// </summary>
    public KeyedCircuitBreakerBrokenException()
        : base(DefaultMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerBrokenException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public KeyedCircuitBreakerBrokenException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerBrokenException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The inner exception.</param>
    public KeyedCircuitBreakerBrokenException(string message, Exception inner)
        : base(message, inner)
    {
    }

    internal KeyedCircuitBreakerBrokenException(string key, TimeSpan retryAfter)
        : base($"The circuit for key '{key}' is now open and is not allowing calls. It can be retried after '{retryAfter}'.")
    {
        Key = key;
        RetryAfter = retryAfter;
    }

#pragma warning disable RS0016 // Add public types and members to the declared API
#if !NETCOREAPP
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyedCircuitBreakerBrokenException"/> class.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
    protected KeyedCircuitBreakerBrokenException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Guard.NotNull(info);

        var enumerator = info.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            if (string.Equals(entry.Name, "RetryAfter", StringComparison.Ordinal))
            {
                RetryAfter = new TimeSpan((long)entry.Value!);
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

        if (RetryAfter.HasValue)
        {
            info.AddValue("RetryAfter", RetryAfter.Value.Ticks);
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
    /// Gets the key whose circuit is broken, if known.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// Gets the amount of time after which the circuit for <see cref="Key"/> allows new attempts, if known.
    /// </summary>
    /// <remarks>
    /// This value is captured when the exception is created and may be inaccurate if consumed at a later time.
    /// </remarks>
    public TimeSpan? RetryAfter { get; }
}
