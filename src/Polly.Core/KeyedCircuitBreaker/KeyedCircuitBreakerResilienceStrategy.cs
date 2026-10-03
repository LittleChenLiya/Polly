namespace Polly.KeyedCircuitBreaker;

/// <summary>
/// A bounded, keyed (grouped) circuit breaker strategy.
/// </summary>
/// <remarks>
/// Each key resolved by the key factory gets an independent group with its own consecutive failure count
/// and broken state. The strategy is fully lazy: it never starts background timers and evaluates the
/// cool-down only when an execution arrives, using the configured <see cref="TimeProvider"/>.
/// </remarks>
internal sealed class KeyedCircuitBreakerResilienceStrategy<T> : ResilienceStrategy<T>
{
    private readonly object _lock = new();
    private readonly Dictionary<string, GroupState> _groups = new(StringComparer.Ordinal);
    private readonly Func<KeyedCircuitBreakerKeyArguments, ValueTask<string>> _keyFactory;
    private readonly Func<KeyedCircuitBreakerPredicateArguments<T>, ValueTask<bool>> _shouldHandle;
    private readonly TimeProvider _timeProvider;
    private readonly int _failureThreshold;
    private readonly TimeSpan _breakDuration;
    private readonly int _maxGroups;
    private long _accessCounter;

    public KeyedCircuitBreakerResilienceStrategy(KeyedCircuitBreakerStrategyOptions<T> options, TimeProvider timeProvider)
    {
        _keyFactory = options.KeyFactory!;
        _shouldHandle = options.ShouldHandle;
        _failureThreshold = options.FailureThreshold;
        _breakDuration = options.BreakDuration;
        _maxGroups = options.MaxGroups;
        _timeProvider = timeProvider;
    }

    protected internal override async ValueTask<Outcome<T>> ExecuteCore<TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<T>>> callback,
        ResilienceContext context,
        TState state)
    {
        // Resolve the key first. If the key factory fails, the user callback is never executed
        // and the failure is not attributed to any group.
        var key = await _keyFactory(new KeyedCircuitBreakerKeyArguments(context)).ConfigureAwait(context.ContinueOnCapturedContext);
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("The key factory of the keyed circuit breaker returned a null or empty key.");
        }

        var group = Admit(key);

        Outcome<T> outcome;
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            outcome = await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            outcome = new(ex);
        }
#pragma warning restore CA1031 // Do not catch general exception types

        // Cancellation is neutral: it never counts as a failure and never resets the group state.
        var isCancellation = outcome.Exception is OperationCanceledException;
        var isFailure = !isCancellation &&
            await _shouldHandle(new KeyedCircuitBreakerPredicateArguments<T>(context, outcome)).ConfigureAwait(context.ContinueOnCapturedContext);

        Complete(group, isFailure, isCancellation);

        return outcome;
    }

    private GroupState Admit(string key)
    {
        lock (_lock)
        {
            if (!_groups.TryGetValue(key, out var group))
            {
                if (_groups.Count >= _maxGroups && !TryEvictIdleGroup())
                {
                    // The capacity is exhausted and every group has executions in flight.
                    // Reject the new group explicitly instead of preempting an active one.
                    throw new KeyedCircuitBreakerCapacityExceededException(key, _maxGroups);
                }

                group = new GroupState();
                _groups[key] = group;
            }

            group.LastAccess = ++_accessCounter;

            if (group.BrokenUntil is DateTimeOffset brokenUntil)
            {
                var now = _timeProvider.GetUtcNow();
                if (now < brokenUntil)
                {
                    throw new KeyedCircuitBreakerBrokenException(key, brokenUntil - now);
                }

                // The cool-down has elapsed, allow a trial execution.
                group.BrokenUntil = null;
            }

            group.InFlight++;
            return group;
        }
    }

    private void Complete(GroupState group, bool isFailure, bool isCancellation)
    {
        lock (_lock)
        {
            group.InFlight--;
            group.LastAccess = ++_accessCounter;

            if (isCancellation)
            {
                return;
            }

            if (isFailure)
            {
                group.ConsecutiveFailures++;
                if (group.ConsecutiveFailures >= _failureThreshold)
                {
                    group.BrokenUntil = _timeProvider.GetUtcNow() + _breakDuration;
                }
            }
            else
            {
                // A successful (not handled) execution resets the group.
                group.ConsecutiveFailures = 0;
                group.BrokenUntil = null;
            }
        }
    }

    private bool TryEvictIdleGroup()
    {
        string? oldestKey = null;
        var oldestAccess = long.MaxValue;

        foreach (var pair in _groups)
        {
            if (pair.Value.InFlight == 0 && pair.Value.LastAccess < oldestAccess)
            {
                oldestAccess = pair.Value.LastAccess;
                oldestKey = pair.Key;
            }
        }

        if (oldestKey is null)
        {
            return false;
        }

        _groups.Remove(oldestKey);
        return true;
    }

    private sealed class GroupState
    {
        public int ConsecutiveFailures { get; set; }

        public int InFlight { get; set; }

        public DateTimeOffset? BrokenUntil { get; set; }

        public long LastAccess { get; set; }
    }
}
