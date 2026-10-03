using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Time.Testing;
using Polly.KeyedCircuitBreaker;
using Polly.Testing;

namespace Polly.Core.Tests.KeyedCircuitBreaker;

/// <summary>
/// Tests for the bounded keyed (grouped) circuit breaker strategy.
/// Run only this class with:
/// <code>dotnet test test/Polly.Core.Tests -f net10.0 --filter "FullyQualifiedName~KeyedCircuitBreakerTests"</code>
/// </summary>
public class KeyedCircuitBreakerTests
{
    private static readonly ResiliencePropertyKey<string> TenantKey = new("tenant");

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ITestOutputHelper _output;
    private int _callbackExecutions;

    public KeyedCircuitBreakerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void GroupIsolation_BreaksAfterThreshold_OtherGroupsUnaffected_ClockAdvanceAllowsRetry()
    {
        var pipeline = CreatePipeline(failureThreshold: 2, breakDuration: TimeSpan.FromSeconds(30));

        // Group A: two consecutive handled failures break the circuit of A only.
        ExecuteFailure(pipeline, "A");
        ExecuteFailure(pipeline, "A");
        _callbackExecutions.ShouldBe(2);

        // Group A is now rejected without executing the user callback.
        var ex = Should.Throw<KeyedCircuitBreakerBrokenException>(() => ExecuteSuccess(pipeline, "A"));
        ex.Key.ShouldBe("A");
        ex.RetryAfter.ShouldBe(TimeSpan.FromSeconds(30));
        _callbackExecutions.ShouldBe(2);

        // Group B does not share state with A and still works.
        ExecuteSuccess(pipeline, "B").ShouldBe("ok");
        _callbackExecutions.ShouldBe(3);

        // Advancing the explicit (fake) clock past the cool-down lets A try again and recover.
        _timeProvider.Advance(TimeSpan.FromSeconds(31));
        ExecuteSuccess(pipeline, "A").ShouldBe("ok");
        _callbackExecutions.ShouldBe(4);
    }

    [Fact]
    public void Success_ResetsConsecutiveFailures()
    {
        var pipeline = CreatePipeline(failureThreshold: 2);

        ExecuteFailure(pipeline, "A");
        ExecuteSuccess(pipeline, "A"); // resets the consecutive failure count
        ExecuteFailure(pipeline, "A"); // first consecutive failure again
        ExecuteSuccess(pipeline, "A"); // circuit still closed

        ExecuteFailure(pipeline, "A");
        ExecuteFailure(pipeline, "A"); // two consecutive failures: circuit breaks

        Should.Throw<KeyedCircuitBreakerBrokenException>(() => ExecuteSuccess(pipeline, "A"));
    }

    [Fact]
    public void Cancellation_DoesNotCountAsFailure()
    {
        // Threshold of 1: a single counted failure would break the circuit.
        var pipeline = CreatePipeline(failureThreshold: 1);

        using var cts = new CancellationTokenSource();
        Should.Throw<OperationCanceledException>(() => ExecuteWithKey(pipeline, "A", token =>
        {
            _callbackExecutions++;
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return "unreachable";
        }, cts.Token));

        // The cancellation was neutral: the group is not broken and the next call executes.
        ExecuteSuccess(pipeline, "A").ShouldBe("ok");
        _callbackExecutions.ShouldBe(2);
    }

    [Fact]
    public void KeyFactoryFailure_DoesNotExecuteCallback()
    {
        var pipeline = CreatePipeline(keyFactory: _ => throw new InvalidTimeZoneException("key factory failed"));

        Should.Throw<InvalidTimeZoneException>(() => ExecuteSuccess(pipeline, "A"));
        _callbackExecutions.ShouldBe(0);
    }

    [Fact]
    public void NullOrEmptyKey_DoesNotExecuteCallback()
    {
        var pipeline = CreatePipeline(keyFactory: _ => new ValueTask<string>((string)null!));

        Should.Throw<InvalidOperationException>(() => ExecuteSuccess(pipeline, "A"));
        _callbackExecutions.ShouldBe(0);
    }

    [Fact]
    public void CapacityFull_AllGroupsInFlight_NewGroupIsRejected_NotPreempted()
    {
        var pipeline = CreatePipeline(maxGroups: 2);
        using var bothStarted = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var started = 0;

        // Concurrency barrier: both callbacks block inside the strategy until released,
        // so groups A and B both have an execution in flight.
        var run = (string key) => Task.Run(() => ExecuteWithKey(pipeline, key, token =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.Set();
            }

            release.Wait(TimeSpan.FromSeconds(30), token).ShouldBeTrue();
            return "done";
        }));

        var taskA = run("A");
        var taskB = run("B");

        bothStarted.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ShouldBeTrue();

        // The capacity is exhausted and no group is idle: the new group C cannot preempt
        // an in-flight group and is explicitly rejected.
        var ex = Should.Throw<KeyedCircuitBreakerCapacityExceededException>(() => ExecuteSuccess(pipeline, "C"));
        ex.Key.ShouldBe("C");
        ex.MaxGroups.ShouldBe(2);
        _callbackExecutions.ShouldBe(0); // the callback of C never ran

        release.Set();
        Task.WaitAll(new Task[] { taskA, taskB }, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        taskA.Result.ShouldBe("done");
        taskB.Result.ShouldBe("done");
    }

    [Fact]
    public void CapacityFull_EvictsIdleGroupsInLeastRecentlyUsedOrder()
    {
        var pipeline = CreatePipeline(failureThreshold: 1, maxGroups: 2);

        ExecuteFailure(pipeline, "A"); // A breaks (threshold 1), A is now the least recently used group
        ExecuteFailure(pipeline, "B"); // B breaks

        // Capacity is 2 and both groups are idle: admitting C evicts the least recently used idle group (A).
        ExecuteSuccess(pipeline, "C").ShouldBe("ok");

        // B kept its state and is still broken.
        Should.Throw<KeyedCircuitBreakerBrokenException>(() => ExecuteSuccess(pipeline, "B"));

        // A was evicted and lost its broken state, so it executes again.
        // Its re-admission evicts the next least recently used idle group (C), keeping B.
        ExecuteSuccess(pipeline, "A").ShouldBe("ok");

        // B was never evicted: it is still broken.
        Should.Throw<KeyedCircuitBreakerBrokenException>(() => ExecuteSuccess(pipeline, "B"));
    }

    [Fact]
    public void Validation_InvalidOptions_Throws()
    {
        Should.Throw<ValidationException>(() => new ResiliencePipelineBuilder<string>()
            .AddKeyedCircuitBreaker(new KeyedCircuitBreakerStrategyOptions<string>()));

        Should.Throw<ValidationException>(() => new ResiliencePipelineBuilder<string>()
            .AddKeyedCircuitBreaker(new KeyedCircuitBreakerStrategyOptions<string> { KeyFactory = ValidKeyFactory, FailureThreshold = 0 }));

        Should.Throw<ValidationException>(() => new ResiliencePipelineBuilder<string>()
            .AddKeyedCircuitBreaker(new KeyedCircuitBreakerStrategyOptions<string> { KeyFactory = ValidKeyFactory, MaxGroups = 0 }));

        Should.Throw<ValidationException>(() => new ResiliencePipelineBuilder<string>()
            .AddKeyedCircuitBreaker(new KeyedCircuitBreakerStrategyOptions<string> { KeyFactory = ValidKeyFactory, BreakDuration = TimeSpan.Zero }));
    }

    [Fact]
    public void Options_Defaults()
    {
        var options = new KeyedCircuitBreakerStrategyOptions<string>();

        options.Name.ShouldBe("KeyedCircuitBreaker");
        options.FailureThreshold.ShouldBe(5);
        options.MaxGroups.ShouldBe(1024);
        options.BreakDuration.ShouldBe(TimeSpan.FromSeconds(5));
        options.ShouldHandle.ShouldNotBeNull();
    }

    [Fact]
    public void AddKeyedCircuitBreaker_StrategyInstanceAdded()
    {
        var builder = new ResiliencePipelineBuilder();
        builder.AddKeyedCircuitBreaker(new KeyedCircuitBreakerStrategyOptions { KeyFactory = ValidKeyFactory });
        builder.Build().GetPipelineDescriptor().FirstStrategy.StrategyInstance.ShouldBeOfType<KeyedCircuitBreakerResilienceStrategy<object>>();

        var genericBuilder = new ResiliencePipelineBuilder<string>();
        genericBuilder.AddKeyedCircuitBreaker(new KeyedCircuitBreakerStrategyOptions<string> { KeyFactory = ValidKeyFactory });
        genericBuilder.Build().GetPipelineDescriptor().FirstStrategy.StrategyInstance.ShouldBeOfType<KeyedCircuitBreakerResilienceStrategy<string>>();
    }

    [Fact]
    public void Demo_ShortCounting()
    {
        var attempts = 0;
        var failures = 0;
        var rejected = 0;
        var pipeline = CreatePipeline(failureThreshold: 2, breakDuration: TimeSpan.FromSeconds(30));

        void TryCall(string key, bool fail)
        {
            try
            {
                ExecuteWithKey(pipeline, key, _ =>
                {
                    attempts++;
                    if (fail)
                    {
                        failures++;
                        throw new InvalidOperationException("boom");
                    }

                    return "ok";
                });
            }
            catch (KeyedCircuitBreakerBrokenException)
            {
                rejected++;
            }
            catch (InvalidOperationException)
            {
                // Expected: the handled failure thrown by the callback above.
            }
        }

        TryCall("A", fail: true); // A: failure #1
        TryCall("A", fail: true); // A: failure #2 -> circuit of A breaks
        TryCall("A", fail: false); // A: rejected, callback not executed
        TryCall("B", fail: false); // B: unaffected, succeeds
        _timeProvider.Advance(TimeSpan.FromSeconds(31));
        TryCall("A", fail: false); // A: cool-down elapsed, trial succeeds and resets

        _output.WriteLine($"attempts={attempts} failures={failures} rejected={rejected}");

        attempts.ShouldBe(4);
        failures.ShouldBe(2);
        rejected.ShouldBe(1);
    }

    private static ValueTask<string> ValidKeyFactory(KeyedCircuitBreakerKeyArguments args)
        => new(args.Context.Properties.GetValue(TenantKey, "unknown"));

    private ResiliencePipeline<string> CreatePipeline(
        int failureThreshold = 2,
        int maxGroups = 10,
        TimeSpan? breakDuration = null,
        Func<KeyedCircuitBreakerKeyArguments, ValueTask<string>>? keyFactory = null)
    {
        var builder = new ResiliencePipelineBuilder<string>
        {
            TimeProvider = _timeProvider,
        };

        return builder
            .AddKeyedCircuitBreaker(new KeyedCircuitBreakerStrategyOptions<string>
            {
                KeyFactory = keyFactory ?? ValidKeyFactory,
                ShouldHandle = args => new ValueTask<bool>(args.Outcome.Exception is InvalidOperationException),
                FailureThreshold = failureThreshold,
                MaxGroups = maxGroups,
                BreakDuration = breakDuration ?? TimeSpan.FromSeconds(30),
            })
            .Build();
    }

    private string ExecuteSuccess(ResiliencePipeline<string> pipeline, string key)
        => ExecuteWithKey(pipeline, key, _ =>
        {
            _callbackExecutions++;
            return "ok";
        });

    private void ExecuteFailure(ResiliencePipeline<string> pipeline, string key)
        => Should.Throw<InvalidOperationException>(() => ExecuteWithKey(pipeline, key, _ =>
        {
            _callbackExecutions++;
            throw new InvalidOperationException("boom");
        }));

    private static string ExecuteWithKey(
        ResiliencePipeline<string> pipeline,
        string key,
        Func<CancellationToken, string> callback,
        CancellationToken cancellationToken = default)
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(TenantKey, key);
        try
        {
            return pipeline.Execute(ctx => callback(ctx.CancellationToken), context);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
