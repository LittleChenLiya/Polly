using Polly.Retry;
using Polly.Telemetry;

namespace Polly.Core.Tests.Telemetry;

public class EventCollectorTests
{
    private const string PipelineName = "test-pipeline";
    private const string InstanceName = "test-instance";
    private const string StrategyName = "test-strategy";

    [Fact]
    public void Collects_Events_In_Order_With_Attempt_And_Result_Category()
    {
        var collector = new EventCollector();
        var pipeline = CreatePipeline(collector, maxRetryAttempts: 2);
        var callCount = 0;

        pipeline.Execute(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new InvalidOperationException("First attempt fails.");
            }
        });

        var snapshot = collector.GetLastSnapshot()!;
        snapshot.ShouldNotBeNull();
        snapshot.IsSealed.ShouldBeTrue();
        snapshot.CallIndex.ShouldBe(0);

        var events = snapshot.Events;
        events.Count.ShouldBeGreaterThanOrEqualTo(4);

        events[0].EventName.ShouldBe("PipelineExecuting");
        events[0].ResultCategory.ShouldBe(ResultCategory.Unknown);

        events[events.Count - 1].EventName.ShouldBe("PipelineExecuted");
        events[events.Count - 1].ResultCategory.ShouldBe(ResultCategory.Success);

        var retryEvents = events.Where(e => e.EventName == "OnRetry").ToList();
        retryEvents.Count.ShouldBe(1);
        retryEvents[0].AttemptNumber.ShouldBe(0);
        retryEvents[0].ResultCategory.ShouldBe(ResultCategory.Exception);
        retryEvents[0].ExceptionType.ShouldBe("System.InvalidOperationException");

        var attemptEvents = events.Where(e => e.EventName == "ExecutionAttempt").ToList();
        attemptEvents.Count.ShouldBe(2);
        attemptEvents[0].AttemptNumber.ShouldBe(0);
        attemptEvents[0].Handled.ShouldBe(true);
        attemptEvents[1].AttemptNumber.ShouldBe(1);
        attemptEvents[1].Handled.ShouldBe(false);

        for (int i = 0; i < events.Count; i++)
        {
            events[i].Sequence.ShouldBe(i);
        }
    }

    [Fact]
    public void Two_Calls_Are_Isolated()
    {
        var collector = new EventCollector();
        var pipeline = CreatePipeline(collector, maxRetryAttempts: 2);

        pipeline.Execute(() => { });
        pipeline.Execute(() => { });

        collector.CallCount.ShouldBe(2);

        var snapshot1 = collector.GetSnapshot(0)!;
        var snapshot2 = collector.GetSnapshot(1)!;

        snapshot1.ShouldNotBeNull();
        snapshot2.ShouldNotBeNull();
        snapshot1.IsSealed.ShouldBeTrue();
        snapshot2.IsSealed.ShouldBeTrue();

        snapshot1.Events[0].EventName.ShouldBe("PipelineExecuting");
        snapshot1.Events[snapshot1.Events.Count - 1].EventName.ShouldBe("PipelineExecuted");
        snapshot2.Events[0].EventName.ShouldBe("PipelineExecuting");
        snapshot2.Events[snapshot2.Events.Count - 1].EventName.ShouldBe("PipelineExecuted");
    }

    [Fact]
    public void Malicious_ToString_Is_Never_Called()
    {
        var collector = new EventCollector();
        var context = ResilienceContextPool.Shared.Get("op-key", TestCancellation.Token);
        var malicious = new MaliciousToStringObject();

        var args = new TelemetryEventArguments<object, MaliciousArgs>(
            new ResilienceTelemetrySource(PipelineName, InstanceName, StrategyName),
            new ResilienceEvent(ResilienceEventSeverity.Warning, "custom-event"),
            context,
            new MaliciousArgs(malicious),
            Outcome.FromResult<object>(42));

        collector.Write(in args);

        malicious.ToStringCallCount.ShouldBe(0);
    }

    [Fact]
    public void Limit_With_Pipeline_Drops_Events_And_Only_Increases_DroppedCount()
    {
        var collector = new EventCollector(maxEventsPerCall: 2);
        var pipeline = CreatePipeline(collector, maxRetryAttempts: 5);

        pipeline.Execute(() => { });

        var snapshot = collector.GetLastSnapshot()!;
        snapshot.ShouldNotBeNull();
        snapshot.TotalCount.ShouldBeGreaterThan(2);
        snapshot.CollectedCount.ShouldBe(2);
        snapshot.DroppedCount.ShouldBe(snapshot.TotalCount - 2);

        snapshot.Events.Count.ShouldBe(2);
        snapshot.Events[0].EventName.ShouldBe("PipelineExecuting");
        snapshot.Events[1].EventName.ShouldBe("ExecutionAttempt");
    }

    [Fact]
    public void Concurrent_Calls_Are_Isolated()
    {
        var collector = new EventCollector();
        var pipeline = CreatePipeline(collector, maxRetryAttempts: 2);
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => pipeline.Execute(() => { })));
        }

        Task.WaitAll([.. tasks]);

        collector.CallCount.ShouldBe(10);

        var snapshots = collector.GetSnapshots();
        snapshots.Count.ShouldBe(10);
        snapshots.ShouldAllBe(s => s.IsSealed);

        foreach (var snapshot in snapshots)
        {
            snapshot.Events[0].EventName.ShouldBe("PipelineExecuting");
            snapshot.Events[snapshot.Events.Count - 1].EventName.ShouldBe("PipelineExecuted");
        }
    }

    [Fact]
    public void Cancellation_Does_Not_Crash_Collector()
    {
        var collector = new EventCollector();
        var pipeline = CreatePipeline(collector, maxRetryAttempts: 2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Should.Throw<OperationCanceledException>(() =>
            pipeline.Execute(ct => { }, cts.Token));

        collector.CallCount.ShouldBe(1);
        var snapshot = collector.GetLastSnapshot()!;
        snapshot.ShouldNotBeNull();
        snapshot.IsSealed.ShouldBeTrue();
        collector.Dispose();
        snapshot.IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void Seal_Prevents_Further_Events()
    {
        var collector = new EventCollector();
        var context = ResilienceContextPool.Shared.Get("op-key", TestCancellation.Token);
        var source = new ResilienceTelemetrySource(PipelineName, InstanceName, StrategyName);

        var executingArgs = new TelemetryEventArguments<object, PipelineExecutingArguments>(
            source,
            new ResilienceEvent(ResilienceEventSeverity.Debug, "PipelineExecuting"),
            context,
            default,
            null);
        collector.Write(in executingArgs);

        collector.Seal();

        var executedArgs = new TelemetryEventArguments<object, PipelineExecutedArguments>(
            source,
            new ResilienceEvent(ResilienceEventSeverity.Information, "PipelineExecuted"),
            context,
            new PipelineExecutedArguments(TimeSpan.FromMilliseconds(10)),
            Outcome.FromResult<object>(42));
        collector.Write(in executedArgs);

        var snapshot = collector.GetLastSnapshot()!;
        snapshot.ShouldNotBeNull();
        snapshot.IsSealed.ShouldBeTrue();
        snapshot.Events.Count.ShouldBe(1);
        snapshot.Events[0].EventName.ShouldBe("PipelineExecuting");
    }

    [Fact]
    public void Collector_Error_Does_Not_Change_Business_Result()
    {
        var collector = new ThrowingTelemetryListener();
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(1)
            })
            .Build();

        var result = pipeline.Execute(() => 42);
        result.ShouldBe(42);
    }

    [Fact]
    public void Dispose_Seals_Active_Snapshots()
    {
        var collector = new EventCollector();
        var context = ResilienceContextPool.Shared.Get("op-key", TestCancellation.Token);
        var source = new ResilienceTelemetrySource(PipelineName, InstanceName, StrategyName);

        var executingArgs = new TelemetryEventArguments<object, PipelineExecutingArguments>(
            source,
            new ResilienceEvent(ResilienceEventSeverity.Debug, "PipelineExecuting"),
            context,
            default,
            null);
        collector.Write(in executingArgs);

        collector.Dispose();

        var snapshot = collector.GetLastSnapshot()!;
        snapshot.ShouldNotBeNull();
        snapshot.IsSealed.ShouldBeTrue();
    }

    private static ResiliencePipeline CreatePipeline(EventCollector collector, int maxRetryAttempts)
    {
        var builder = new ResiliencePipelineBuilder { TelemetryListener = collector };
        return builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(1)
            })
            .Build();
    }

    private sealed class MaliciousToStringObject
    {
        public int ToStringCallCount { get; private set; }

        public override string ToString()
        {
            ToStringCallCount++;
            throw new InvalidOperationException("Malicious ToString!");
        }
    }

    private sealed class MaliciousArgs
    {
        public MaliciousArgs(MaliciousToStringObject malicious)
        {
            Malicious = malicious;
        }

        public MaliciousToStringObject Malicious { get; }
    }

    private sealed class ThrowingTelemetryListener : TelemetryListener
    {
        public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
        {
            throw new InvalidOperationException("Collector error!");
        }
    }
}
