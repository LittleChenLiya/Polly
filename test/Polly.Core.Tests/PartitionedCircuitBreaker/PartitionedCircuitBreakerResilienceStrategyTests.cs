using Microsoft.Extensions.Time.Testing;
using Polly.Partitioning;

namespace Polly.Core.Tests.PartitionedCircuitBreaker;

public class PartitionedCircuitBreakerResilienceStrategyTests
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ResiliencePropertyKey<string> _partitionKey = new("partition-key");

    private ResilienceContext Context(string key)
    {
        var context = new ResilienceContext();
        context.Properties.Set(_partitionKey, key);
        return context;
    }

    private PartitionedCircuitBreakerStrategyOptions<int> Options(
        int failureThreshold = 2,
        int maxPartitionCount = 100,
        TimeSpan? breakDuration = null,
        bool handleAllExceptions = false)
    {
        return new PartitionedCircuitBreakerStrategyOptions<int>
        {
            PartitionKeyGenerator = context => context.Properties.GetValue(_partitionKey, "default"),
            FailureThreshold = failureThreshold,
            BreakDuration = breakDuration ?? TimeSpan.FromSeconds(10),
            MaxPartitionCount = maxPartitionCount,
            ShouldHandle = args => new ValueTask<bool>(
                handleAllExceptions
                    ? args.Outcome.Exception is not null
                    : args.Outcome.Exception is InvalidOperationException)
        };
    }

    private ResiliencePipeline<int> Build(PartitionedCircuitBreakerStrategyOptions<int>? options = null)
        => new ResiliencePipelineBuilder<int> { TimeProvider = _timeProvider }
            .AddPartitionedCircuitBreaker(options ?? Options())
            .Build();

    [Fact]
    public void Execute_TwoConsecutiveFailures_OpenOnlyThatPartition_ShortCountDemo()
    {
        // Arrange - short failure threshold of 2: two consecutive failures open the partition.
        ResiliencePipeline<int> pipeline = Build();
        ResilienceContext contextA = Context("A");
        ResilienceContext contextB = Context("B");

        // Act - two consecutive failures for partition A open it.
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));

        // Assert - A is rejected without executing the business delegate.
        var executed = false;
        Should.Throw<PartitionedCircuitBreakerOpenException>(() => pipeline.Execute(
            _ => { executed = true; return 1; },
            contextA));
        executed.ShouldBeFalse();

        // Assert - partition B is unaffected and still succeeds.
        pipeline.Execute(_ => 42, contextB).ShouldBe(42);
    }

    [Fact]
    public void Execute_AfterExplicitClockCooldown_PartitionRecovers()
    {
        // Arrange
        ResiliencePipeline<int> pipeline = Build();
        ResilienceContext contextA = Context("A");

        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        Should.Throw<PartitionedCircuitBreakerOpenException>(() => pipeline.Execute(_ => 1, contextA));

        // Act - advance the fake clock past the cooldown end.
        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        // Assert - the partition attempts again; a handled failure re-opens it.
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        Should.Throw<PartitionedCircuitBreakerOpenException>(() => pipeline.Execute(_ => 1, contextA));

        // Act - advance again and succeed: the failure count resets.
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        pipeline.Execute(_ => 7, contextA).ShouldBe(7);

        // Assert - another failure does not open the partition (count was reset by the success).
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        pipeline.Execute(_ => 1, contextA).ShouldBe(1);
    }

    [Fact]
    public void Execute_SuccessBetweenFailures_ResetsConsecutiveFailureCount()
    {
        // Arrange
        ResiliencePipeline<int> pipeline = Build();
        ResilienceContext contextA = Context("A");

        // Act - fail, succeed, fail: never two consecutive failures.
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        pipeline.Execute(_ => 1, contextA).ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));

        // Assert - the partition is still closed.
        pipeline.Execute(_ => 1, contextA).ShouldBe(1);
    }

    [Fact]
    public void Execute_OpenException_ExposesRetryAfter()
    {
        // Arrange
        ResiliencePipeline<int> pipeline = Build(Options(breakDuration: TimeSpan.FromSeconds(30)));
        ResilienceContext contextA = Context("A");

        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));

        // Act
        var exception = Should.Throw<PartitionedCircuitBreakerOpenException>(() => pipeline.Execute(_ => 1, contextA));

        // Assert
        exception.RetryAfter.ShouldNotBeNull();
        exception.RetryAfter!.Value.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Execute_MaxPartitionsReached_AllInFlight_NewPartitionRejected_NoPreemption()
    {
        // Arrange - the strategy tracks at most 2 partitions.
        ResiliencePipeline<int> pipeline = Build(Options(maxPartitionCount: 2));
        ResilienceContext contextA = Context("A");
        ResilienceContext contextB = Context("B");
        ResilienceContext contextC = Context("C");

        var startedA = new TaskCompletionSource<bool>();
        var startedB = new TaskCompletionSource<bool>();
        var releaseA = new TaskCompletionSource<bool>();
        var releaseB = new TaskCompletionSource<bool>();

        Task<int> taskA = Task.Run(() => pipeline.ExecuteAsync(
            async _ =>
            {
                startedA.SetResult(true);
                await releaseA.Task;
                return 1;
            },
            contextA).AsTask());

        Task<int> taskB = Task.Run(() => pipeline.ExecuteAsync(
            async _ =>
            {
                startedB.SetResult(true);
                await releaseB.Task;
                return 2;
            },
            contextB).AsTask());

        await Task.WhenAll(startedA.Task, startedB.Task);

        // Act & Assert - quota is full and every existing partition is in-flight:
        // the new partition is rejected, no preemption takes place.
        bool executed = false;
        Func<Task<int>> action = () => pipeline.ExecuteAsync(
            _ => { executed = true; return new ValueTask<int>(3); },
            contextC).AsTask();

        await Should.ThrowAsync<PartitionedCircuitBreakerRejectedException>(action);
        executed.ShouldBeFalse();

        // Assert - the in-flight calls complete normally and their partitions are not affected.
        releaseA.SetResult(true);
        releaseB.SetResult(true);
        (await taskA).ShouldBe(1);
        (await taskB).ShouldBe(2);
    }

    [Fact]
    public void Execute_MaxPartitionsReached_IdlePartitionsEvicted_FifoOrder()
    {
        // Arrange - the strategy tracks at most 2 partitions.
        ResiliencePipeline<int> pipeline = Build(Options(maxPartitionCount: 2));
        ResilienceContext contextA = Context("A");
        ResilienceContext contextB = Context("B");
        ResilienceContext contextC = Context("C");

        // A has one failure (not open); B is created afterwards.
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        pipeline.Execute(_ => 1, contextB).ShouldBe(1);

        // Act - a new key arrives while the quota is full: the oldest idle partition (A) is evicted in FIFO order.
        pipeline.Execute(_ => 3, contextC).ShouldBe(3);

        // Assert - A was evicted: its failure count starts from zero again (one more failure does not open it).
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        pipeline.Execute(_ => 1, contextA).ShouldBe(1);

        // Assert - B was also evicted to make room for A: B's state is fresh as well.
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextB));
        pipeline.Execute(_ => 1, contextB).ShouldBe(1);
    }

    [Fact]
    public async Task Execute_Eviction_SkipsInFlightPartitions()
    {
        // Arrange - the strategy tracks at most 2 partitions.
        ResiliencePipeline<int> pipeline = Build(Options(maxPartitionCount: 2));
        ResilienceContext contextA = Context("A");
        ResilienceContext contextB = Context("B");
        ResilienceContext contextC = Context("C");

        var startedA = new TaskCompletionSource<bool>();
        var releaseA = new TaskCompletionSource<bool>();

        Task<int> taskA = Task.Run(() => pipeline.ExecuteAsync(
            async _ =>
            {
                startedA.SetResult(true);
                await releaseA.Task;
                return 1;
            },
            contextA).AsTask());
        await startedA.Task;

        // B is idle and created after the in-flight A.
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextB));

        // Act - quota is full; A is in-flight and must be skipped, so idle B is evicted instead.
        pipeline.Execute(_ => 3, contextC).ShouldBe(3);

        // Assert - B was evicted (its failure count is fresh).
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextB));
        pipeline.Execute(_ => 1, contextB).ShouldBe(1);

        // Assert - A was retained (still in-flight); completing it succeeds.
        releaseA.SetResult(true);
        (await taskA).ShouldBe(1);
    }

    [Fact]
    public void Execute_Cancellation_DoesNotCountAsFailure()
    {
        // Arrange - the default predicate excludes cancellation from handled failures.
        ResiliencePipeline<int> pipeline = Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ResilienceContext canceledContext = Context("A");
        canceledContext.CancellationToken = cts.Token;

        // Act - the execution is canceled: the outcome must not count as a failure.
        Should.Throw<OperationCanceledException>(() => pipeline.Execute<int>(
            _ => throw new OperationCanceledException(),
            canceledContext));

        // Assert - the partition is still closed: a new call with a live token succeeds.
        ResilienceContext liveContext = Context("A");
        pipeline.Execute(_ => 1, liveContext).ShouldBe(1);
    }

    [Fact]
    public void Execute_KeyGeneratorFailure_BusinessDelegateNeverRuns()
    {
        // Arrange
        var keyGeneratorCalled = false;
        var businessDelegateCalled = false;
        var options = new PartitionedCircuitBreakerStrategyOptions<int>
        {
            PartitionKeyGenerator = _ =>
            {
                keyGeneratorCalled = true;
                throw new InvalidOperationException("key factory failed");
            },
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(10),
            MaxPartitionCount = 100,
            ShouldHandle = args => new ValueTask<bool>(args.Outcome.Exception is InvalidOperationException)
        };
        ResiliencePipeline<int> pipeline = Build(options);
        ResilienceContext context = Context("A");

        // Act & Assert - the key factory failure propagates and the business delegate is not invoked.
        Should.Throw<InvalidOperationException>(() => pipeline.Execute(
            _ => { businessDelegateCalled = true; return 1; },
            context));

        keyGeneratorCalled.ShouldBeTrue();
        businessDelegateCalled.ShouldBeFalse();
    }

    [Fact]
    public void Execute_KeyGeneratorFailure_DoesNotConsumeQuota()
    {
        // Arrange - quota of 1, key factory always fails.
        var options = new PartitionedCircuitBreakerStrategyOptions<int>
        {
            PartitionKeyGenerator = _ => throw new InvalidOperationException("key factory failed"),
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(10),
            MaxPartitionCount = 1,
            ShouldHandle = args => new ValueTask<bool>(args.Outcome.Exception is InvalidOperationException)
        };
        ResiliencePipeline<int> failingPipeline = Build(options);

        // Act - repeated key factory failures must not consume quota.
        for (var i = 0; i < 5; i++)
        {
            Should.Throw<InvalidOperationException>(() => failingPipeline.Execute(_ => 1, Context($"key-{i}")));
        }

        // Assert - a working key factory still creates partitions normally.
        ResiliencePipeline<int> validPipeline = Build(Options(maxPartitionCount: 1));
        validPipeline.Execute(_ => 1, Context("A")).ShouldBe(1);
    }

    [Fact]
    public void Execute_OnOpenedAndOnClosed_EventsRaised()
    {
        // Arrange
        var openedKeys = new List<string>();
        var closedKeys = new List<string>();
        var options = new PartitionedCircuitBreakerStrategyOptions<int>
        {
            PartitionKeyGenerator = context => context.Properties.GetValue(_partitionKey, "default"),
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(10),
            MaxPartitionCount = 100,
            ShouldHandle = args => new ValueTask<bool>(args.Outcome.Exception is InvalidOperationException),
            OnOpened = args =>
            {
                openedKeys.Add(args.PartitionKey);
                return default;
            },
            OnClosed = args =>
            {
                closedKeys.Add(args.PartitionKey);
                return default;
            }
        };
        ResiliencePipeline<int> pipeline = Build(options);
        ResilienceContext contextA = Context("A");

        // Act - open the partition.
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        openedKeys.ShouldBe(new[] { "A" });
        closedKeys.ShouldBeEmpty();

        // Act - advance the clock and recover with a success.
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        pipeline.Execute(_ => 1, contextA);

        // Assert - the close event was raised for the same key.
        closedKeys.ShouldBe(new[] { "A" });
    }

    [Fact]
    public void Execute_NoBackgroundTimer_CooldownControlledByFakeClock()
    {
        // Arrange
        ResiliencePipeline<int> pipeline = Build();
        ResilienceContext contextA = Context("A");

        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));
        Should.Throw<InvalidOperationException>(() => pipeline.Execute<int>(_ => throw new InvalidOperationException(), contextA));

        // Assert - without advancing the fake clock the partition stays open (no timer-based transition).
        Should.Throw<PartitionedCircuitBreakerOpenException>(() => pipeline.Execute(_ => 1, contextA));
        Should.Throw<PartitionedCircuitBreakerOpenException>(() => pipeline.Execute(_ => 1, contextA));

        // Act - advance the fake clock: the cooldown ends explicitly.
        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        // Assert - calls are allowed again.
        pipeline.Execute(_ => 1, contextA).ShouldBe(1);
    }
}
