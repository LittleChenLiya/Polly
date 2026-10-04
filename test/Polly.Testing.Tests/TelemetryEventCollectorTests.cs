using Polly.Retry;
using Polly.Telemetry;

namespace Polly.Testing.Tests;

public class TelemetryEventCollectorTests
{
    [Fact]
    public void Retry_FailureThenSuccess_ProducesStableEventSequence()
    {
        // Arrange
        var collector = new TelemetryEventCollector();
        var pipeline = CreateRetryPipeline(collector);
        var calls = 0;

        // Act
        string result;
        using (collector.BeginInvocation())
        {
            result = pipeline.Execute(_ => ++calls == 1 ? throw new InvalidOperationException("boom") : "ok", TestContext.Current.CancellationToken);
        }

        // Assert
        result.ShouldBe("ok");
        var snapshot = collector.GetSnapshots().ShouldHaveSingleItem();
        snapshot.DroppedEvents.ShouldBe(0);

        // the event names and their order are stable
        snapshot.Events.Select(e => e.EventName).ShouldBe(
        [
            "PipelineExecuting",
            "ExecutionAttempt",
            "OnRetry",
            "ExecutionAttempt",
            "PipelineExecuted",
        ]);
        snapshot.Events.Select(e => e.Sequence).ShouldBe([0, 1, 2, 3, 4]);
        snapshot.Events.Select(e => e.Attempt).ShouldBe([null, 0, 0, 1, null]);

        // the failed attempt
        var failedAttempt = snapshot.Events[1];
        failedAttempt.Severity.ShouldBe(ResilienceEventSeverity.Warning);
        failedAttempt.Outcome.ShouldBe(TelemetryOutcomeCategory.Exception);
        failedAttempt.ExceptionType.ShouldBe(typeof(InvalidOperationException).FullName);

        // the retry notification
        var onRetry = snapshot.Events[2];
        onRetry.Outcome.ShouldBe(TelemetryOutcomeCategory.Exception);
        onRetry.Arguments["AttemptNumber"].Value.ShouldBe(0);
        onRetry.Arguments["RetryDelay"].Value.ShouldBe(TimeSpan.Zero);
        onRetry.Arguments["Context"].Kind.ShouldBe(CapturedValueKind.TypeMarker);

        // the successful attempt
        var successfulAttempt = snapshot.Events[3];
        successfulAttempt.Severity.ShouldBe(ResilienceEventSeverity.Information);
        successfulAttempt.Outcome.ShouldBe(TelemetryOutcomeCategory.Result);
        successfulAttempt.Result.Kind.ShouldBe(CapturedValueKind.Scalar);
        successfulAttempt.Result.Value.ShouldBe("ok");

        // the pipeline completion
        snapshot.Events[4].Outcome.ShouldBe(TelemetryOutcomeCategory.Result);
        snapshot.Events[4].Result.Value.ShouldBe("ok");
    }

    [Fact]
    public void Invocations_AreIsolated()
    {
        // Arrange
        var collector = new TelemetryEventCollector();
        var pipeline = CreateRetryPipeline(collector);

        // Act
        using (collector.BeginInvocation())
        {
            var calls = 0;
            pipeline.Execute(_ => ++calls == 1 ? throw new InvalidOperationException("boom") : "ok", TestContext.Current.CancellationToken);
        }

        using (collector.BeginInvocation())
        {
            pipeline.Execute(_ => "ok", TestContext.Current.CancellationToken);
        }

        // Assert
        var snapshots = collector.GetSnapshots();
        snapshots.Count.ShouldBe(2);

        // the first invocation failed once and then succeeded
        snapshots[0].Events.Select(e => e.EventName).ShouldBe(
            ["PipelineExecuting", "ExecutionAttempt", "OnRetry", "ExecutionAttempt", "PipelineExecuted"]);

        // the second invocation has no events from the first one
        snapshots[1].Events.Select(e => e.EventName).ShouldBe(
            ["PipelineExecuting", "ExecutionAttempt", "PipelineExecuted"]);
        snapshots[1].Events.Select(e => e.Sequence).ShouldBe([0, 1, 2]);
        snapshots[1].Events.ShouldAllBe(e => e.Outcome != TelemetryOutcomeCategory.Exception);
    }

    [Fact]
    public void MaliciousToString_IsNeverCalled_AndObjectIsTypeMarked()
    {
        // Arrange
        MaliciousToString.ToStringCallCount = 0;
        var malicious = new MaliciousToString();
        var collector = new TelemetryEventCollector();

        // Act
        using (collector.BeginInvocation())
        {
            WriteEvent(collector, "CustomEvent", malicious, Outcome.FromResult<object?>(malicious));
        }

        // Assert
        MaliciousToString.ToStringCallCount.ShouldBe(0);

        var snapshot = collector.GetSnapshots().ShouldHaveSingleItem();
        var captured = snapshot.Events.ShouldHaveSingleItem();

        // the malicious result is only marked by its type name
        captured.Result.Kind.ShouldBe(CapturedValueKind.TypeMarker);
        captured.Result.TypeName.ShouldBe(typeof(MaliciousToString).FullName);
        captured.Result.Value.ShouldBeNull();

        // the malicious arguments object is never stored, only its scalar properties are copied
        captured.Arguments["CallCount"].Value.ShouldBe(0);
        captured.Arguments.Values.ShouldNotContain(v => ReferenceEquals(v.Value, malicious));
    }

    [Fact]
    public void CapacityLimit_ExtraEventsOnlyIncrementDropped()
    {
        // Arrange
        MaliciousToString.ToStringCallCount = 0;
        var collector = new TelemetryEventCollector(maxEventsPerInvocation: 2);

        // Act
        using (collector.BeginInvocation())
        {
            WriteEvent(collector, "E1");
            WriteEvent(collector, "E2");
            WriteEvent(collector, "E3", new MaliciousToString());
        }

        // Assert
        var snapshot = collector.GetSnapshots().ShouldHaveSingleItem();

        // the third event only increments the dropped counter and is not stored
        snapshot.Events.Select(e => e.EventName).ShouldBe(["E1", "E2"]);
        snapshot.DroppedEvents.ShouldBe(1);
        MaliciousToString.ToStringCallCount.ShouldBe(0);
    }

    [Fact]
    public void Constructor_InvalidLimit_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new TelemetryEventCollector(maxEventsPerInvocation: 0));

    [Fact]
    public async Task ConcurrentInvocations_AreIsolated()
    {
        // Arrange
        var collector = new TelemetryEventCollector();
        var pipeline = CreateRetryPipeline(collector);
        using var barrier = new Barrier(2);

        // Act
        var first = Task.Run(() => RunInvocation<InvalidOperationException>(), TestContext.Current.CancellationToken);
        var second = Task.Run(() => RunInvocation<ApplicationException>(), TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second);

        // Assert
        var firstEvents = first.Result.Snapshot.Events;
        var secondEvents = second.Result.Snapshot.Events;

        firstEvents.Count.ShouldBe(5);
        secondEvents.Count.ShouldBe(5);

        // each invocation only contains its own events
        firstEvents.Where(e => e.ExceptionType is not null).ShouldAllBe(e => e.ExceptionType == typeof(InvalidOperationException).FullName);
        secondEvents.Where(e => e.ExceptionType is not null).ShouldAllBe(e => e.ExceptionType == typeof(ApplicationException).FullName);
        firstEvents.Select(e => e.Sequence).ShouldBe([0, 1, 2, 3, 4]);
        secondEvents.Select(e => e.Sequence).ShouldBe([0, 1, 2, 3, 4]);

        collector.GetSnapshots().Count.ShouldBe(2);

        TelemetryInvocation RunInvocation<TException>()
            where TException : Exception, new()
        {
            var invocation = collector.BeginInvocation();
            var calls = 0;
            pipeline.Execute(_ =>
            {
                if (++calls == 1)
                {
                    // force both invocations to be in-flight at the same time
                    barrier.SignalAndWait(CancellationToken.None);
                    throw new TException();
                }

                return "ok";
            },
            TestContext.Current.CancellationToken);
            invocation.Dispose();
            return invocation;
        }
    }

    [Fact]
    public async Task Cancellation_SnapshotIsSealed_AndBusinessExceptionIsUnchanged()
    {
        // Arrange
        var collector = new TelemetryEventCollector();
        var pipeline = CreateRetryPipeline(collector, delay: TimeSpan.FromMinutes(10));
        using var cancellation = new CancellationTokenSource();

        // Act
        TelemetryInvocation invocation;
        using (invocation = collector.BeginInvocation())
        {
            await Should.ThrowAsync<OperationCanceledException>(async () =>
                await pipeline.ExecuteAsync<string>(
                    async _ =>
                    {
                        await Task.Yield();
#pragma warning disable S6966 // CancelAsync is not available on all target frameworks.
                        cancellation.Cancel();
#pragma warning restore S6966
                        throw new InvalidOperationException("boom");
                    },
                    cancellation.Token));
        }

        // Assert: the invocation is sealed even though the execution was canceled
        invocation.IsSealed.ShouldBeTrue();
        var snapshot = invocation.Snapshot;
        snapshot.Events.Select(e => e.EventName).ShouldBe(
            ["PipelineExecuting", "ExecutionAttempt", "OnRetry", "PipelineExecuted"]);

        // the cancellation is visible as an exception outcome, the collector did not interfere
        var lastEvent = snapshot.Events[snapshot.Events.Count - 1];
        lastEvent.Outcome.ShouldBe(TelemetryOutcomeCategory.Exception);
        lastEvent.ExceptionType.ShouldBe(typeof(OperationCanceledException).FullName);
    }

    [Fact]
    public void Sealing_SnapshotIsStable_AndLateEventsAreIgnored()
    {
        // Arrange
        var collector = new TelemetryEventCollector();
        var invocation = collector.BeginInvocation();
        WriteEvent(collector, "E1");

        // Assert: the snapshot is not available before the invocation is sealed
        invocation.IsSealed.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => invocation.Snapshot);

        // Act
        invocation.Dispose();

        // Assert
        invocation.IsSealed.ShouldBeTrue();
        var snapshot = invocation.Snapshot;
        snapshot.Events.Select(e => e.EventName).ShouldBe(["E1"]);

        // late events are not collected anymore
        WriteEvent(collector, "E2");
        snapshot.Events.Count.ShouldBe(1);

        // disposing again is a no-op
        invocation.Dispose();
        collector.GetSnapshots().Count.ShouldBe(1);
    }

    [Fact]
    public void InnerListener_KeepsReceivingEvents_AndListenerFailuresDoNotAffectPipeline()
    {
        // Arrange
        var received = new List<string>();
        var inner = new CallbackListener(args =>
        {
            received.Add(args.EventName);
            throw new InvalidOperationException("listener failure");
        });
        var collector = new TelemetryEventCollector(inner);
        var pipeline = CreateRetryPipeline(collector);

        // Act
        string result;
        using (collector.BeginInvocation())
        {
            result = pipeline.Execute(_ => "ok", TestContext.Current.CancellationToken);
        }

        // Assert: the failing listener did not change the business result
        result.ShouldBe("ok");

        // the original listener kept receiving every event
        received.ShouldBe(["PipelineExecuting", "ExecutionAttempt", "PipelineExecuted"]);

        // and the collector collected them too
        collector.GetSnapshots().ShouldHaveSingleItem().Events.Count.ShouldBe(3);
    }

    [Fact]
    public void UnknownObjects_AreTypeMarked_AndMutableOriginalsAreNotStored()
    {
        // Arrange
        var payload = new MutablePayload { Value = 1 };
        var args = new CustomArguments { AttemptNumber = 7, Payload = payload };
        var collector = new TelemetryEventCollector();
        var pipeline = new ResiliencePipelineBuilder<string> { TelemetryListener = collector }
            .AddStrategy(context => new ReportingStrategy(context.Telemetry, args), new TestOptions { Name = "reporting" })
            .Build();

        // Act
        using (collector.BeginInvocation())
        {
            pipeline.Execute(_ => "ok", TestContext.Current.CancellationToken).ShouldBe("ok");
        }

        // mutate the originals after the collection
        payload.Value = 999;
        args.AttemptNumber = 999;

        // Assert
        var snapshot = collector.GetSnapshots().ShouldHaveSingleItem();
        var captured = snapshot.Events.Single(e => e.EventName == "CustomEvent");
        captured.StrategyName.ShouldBe("reporting");

        // explicitly supported scalar fields are copied with the values they had at capture time
        captured.Attempt.ShouldBe(7);
        captured.Arguments["AttemptNumber"].Value.ShouldBe(7);

        // unknown objects are only marked by their type name, the mutable original is not stored
        var capturedPayload = captured.Arguments["Payload"];
        capturedPayload.Kind.ShouldBe(CapturedValueKind.TypeMarker);
        capturedPayload.TypeName.ShouldBe(typeof(MutablePayload).FullName);
        capturedPayload.Value.ShouldBeNull();
        captured.Arguments.Values.ShouldNotContain(v => ReferenceEquals(v.Value, payload));
        captured.Arguments.Values.ShouldNotContain(v => ReferenceEquals(v.Value, args));
    }

    [Fact]
    public void Events_WithoutActiveInvocation_AreNotCollected_ButStillForwarded()
    {
        // Arrange
        var received = 0;
        var collector = new TelemetryEventCollector(new CallbackListener(_ => received++));
        var pipeline = CreateRetryPipeline(collector);

        // Act
        pipeline.Execute(_ => "ok", TestContext.Current.CancellationToken).ShouldBe("ok");

        // Assert
        collector.GetSnapshots().ShouldBeEmpty();
        received.ShouldBe(3);
    }

    private static ResiliencePipeline<string> CreateRetryPipeline(TelemetryEventCollector collector, TimeSpan? delay = null) =>
        new ResiliencePipelineBuilder<string> { TelemetryListener = collector }
            .AddRetry(new RetryStrategyOptions<string>
            {
                MaxRetryAttempts = 3,
                Delay = delay ?? TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .Build();

    private static void WriteEvent(TelemetryEventCollector collector, string eventName, object? args = null, Outcome<object?>? outcome = null)
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            collector.Write<object?, object?>(new TelemetryEventArguments<object?, object?>(
                new ResilienceTelemetrySource("test-pipeline", "test-instance", "test-strategy"),
                new ResilienceEvent(ResilienceEventSeverity.Warning, eventName),
                context,
                args,
                outcome));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private sealed class CallbackListener(Action<ResilienceEvent> callback) : TelemetryListener
    {
        public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args) => callback(args.Event);
    }

    private sealed class ReportingStrategy(ResilienceStrategyTelemetry telemetry, object args) : ResilienceStrategy
    {
        protected override async ValueTask<Outcome<TResult>> ExecuteCore<TResult, TState>(
            Func<ResilienceContext, TState, ValueTask<Outcome<TResult>>> callback,
            ResilienceContext context,
            TState state)
        {
            telemetry.Report(new ResilienceEvent(ResilienceEventSeverity.Warning, "CustomEvent"), context, args);
            return await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
        }
    }

    private sealed class TestOptions : ResilienceStrategyOptions;

    private sealed class CustomArguments
    {
        public int AttemptNumber { get; set; }

        public MutablePayload? Payload { get; set; }
    }

    private sealed class MutablePayload
    {
        public int Value { get; set; }
    }

    private sealed class MaliciousToString
    {
        public static int ToStringCallCount { get; set; }

#pragma warning disable CA1822, S1144 // Read by the collector through reflection.
        public int CallCount => ToStringCallCount;
#pragma warning restore CA1822, S1144

        public override string ToString()
        {
            ToStringCallCount++;
            throw new InvalidOperationException("ToString must never be called by the collector.");
        }
    }
}
