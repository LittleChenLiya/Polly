using Microsoft.Extensions.Time.Testing;
using Polly.Retry;
using Polly.Telemetry;

namespace Polly.Core.Tests.Retry;

public class RetryResilienceStrategyBudgetTests
{
    private readonly RetryStrategyOptions<string> _options = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<TelemetryEventArguments<object, object>> _args = [];
    private readonly ResilienceStrategyTelemetry _telemetry;

    public RetryResilienceStrategyBudgetTests()
    {
        _telemetry = TestUtilities.CreateResilienceTelemetry(_args.Add);
        _options.ShouldHandle = _ => PredicateResult.True();
        _options.Randomizer = () => 1;
        _options.BackoffType = DelayBackoffType.Constant;
    }

    [Fact]
    public void ExecuteAsync_BudgetTooSmallForNextDelay_DoesNotStartNextAttempt()
    {
        // budget = 100ms, first call = 40ms, ShouldHandle = 20ms, planned delay = 50ms
        // 40 + 20 + 50 = 110 > 100, so the second call must not start
        _options.MaxTotalRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(20));
            return PredicateResult.True();
        };

        var executions = 0;
        var sut = CreateSut(_options);

        var result = sut.Execute(_ =>
        {
            executions++;
            _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
            return $"result-{executions}";
        },
        TestCancellation.Token);

        executions.ShouldBe(1);
        result.ShouldBe("result-1");
    }

    [Fact]
    public async Task ExecuteAsync_RemainingBudgetExactlyEqualsDelay_WaitsButDoesNotStartNextAttempt()
    {
        // budget = 100ms, first call = 40ms, ShouldHandle = 10ms, planned delay = 50ms
        // 40 + 10 + 50 = 100 == budget, so the delay is awaited,
        // but after waking up no budget remains and the next attempt must not start
        _options.MaxTotalRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return PredicateResult.True();
        };

        var executions = 0;
        var sut = CreateSut(_options);

        var executing = sut.ExecuteAsync(_ =>
        {
            executions++;
            _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
            return new ValueTask<string>($"result-{executions}");
        },
        TestCancellation.Token).AsTask();

        _timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        var result = await executing;

        executions.ShouldBe(1);
        result.ShouldBe("result-1");
    }

    [Fact]
    public void ExecuteAsync_ElapsedTimeAccumulatesAcrossAllStages()
    {
        // budget = 100ms, call = 30ms, ShouldHandle = 10ms, DelayGenerator = 10ms, OnRetry = 10ms, delay = 50ms
        // 30 + 10 + 10 + 10 + 50 = 110 > 100, so the second call must not start
        _options.MaxTotalRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return PredicateResult.True();
        };
        _options.DelayGenerator = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return new ValueTask<TimeSpan?>(TimeSpan.FromMilliseconds(50));
        };
        _options.OnRetry = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return default;
        };

        var executions = 0;
        var sut = CreateSut(_options);

        sut.Execute(_ =>
        {
            executions++;
            _timeProvider.Advance(TimeSpan.FromMilliseconds(30));
            return "result";
        },
        TestCancellation.Token);

        executions.ShouldBe(1);
    }

    [Fact]
    public void ExecuteAsync_OnRetryDurationCountsTowardsBudget()
    {
        // budget = 100ms, call = 20ms, OnRetry = 60ms, delay = 30ms
        // 20 + 60 + 30 = 110 > 100, so the second call must not start
        _options.MaxTotalRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(30);
        _options.OnRetry = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(60));
            return default;
        };

        var executions = 0;
        var sut = CreateSut(_options);

        sut.Execute(_ =>
        {
            executions++;
            _timeProvider.Advance(TimeSpan.FromMilliseconds(20));
            return "result";
        },
        TestCancellation.Token);

        executions.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_SufficientBudget_RetriesNormally()
    {
        _options.MaxTotalRetryDuration = TimeSpan.FromHours(1);
        _options.Delay = TimeSpan.FromMilliseconds(10);
        _options.MaxRetryAttempts = 3;

        var executions = 0;
        var sut = CreateSut(_options);

        var executing = sut.ExecuteAsync(_ =>
        {
            executions++;
            return new ValueTask<string>("result");
        },
        TestCancellation.Token).AsTask();

        while (!executing.IsCompleted)
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        }

        (await executing).ShouldBe("result");
        executions.ShouldBe(4);
        _args.ShouldAllBe(a => a.Event.EventName != RetryConstants.MaxTotalRetryDurationExceededEvent);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationTakesPrecedenceOverBudget()
    {
        _options.MaxTotalRetryDuration = TimeSpan.FromHours(1);
        _options.Delay = TimeSpan.FromMilliseconds(50);

        using var cancellation = _timeProvider.CreateCancellationTokenSource(_options.Delay);
        var executions = 0;
        var sut = CreateSut(_options);

        var resultTask = sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                return Outcome.FromResultAsValueTask("result");
            },
            ResilienceContextPool.Shared.Get(cancellation.Token),
            default(object));

        _timeProvider.Advance(_options.Delay);
        var result = await resultTask;

        result.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        executions.ShouldBe(1);
    }

    [Fact]
    public void ExecuteAsync_BudgetExhausted_LastResultNotDisposed()
    {
        var options = CreateDisposableOptions();
        options.ShouldHandle = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(20));
            return PredicateResult.True();
        };

        var results = new List<CountingDisposableResult>();
        var sut = CreateSut(options);

        var outcome = sut.Execute(_ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
            var result = new CountingDisposableResult();
            results.Add(result);
            return result;
        },
        TestCancellation.Token);

        results.Count.ShouldBe(1);
        outcome.ShouldBeSameAs(results[0]);
        results[0].DisposeCount.ShouldBe(0);
        results[0].IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_BudgetExhaustedAfterDelay_LastResultNotDisposedAndReturned()
    {
        // remaining budget exactly fits the delay, so the delay happens,
        // but the next attempt does not start and the last result is returned undisposed
        var options = CreateDisposableOptions();
        options.ShouldHandle = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(50));
            return PredicateResult.True();
        };

        var results = new List<CountingDisposableResult>();
        var sut = CreateSut(options);

        var executing = sut.ExecuteAsync(_ =>
        {
            var result = new CountingDisposableResult();
            results.Add(result);
            return new ValueTask<CountingDisposableResult>(result);
        },
        TestCancellation.Token).AsTask();

        _timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        var outcome = await executing;

        results.Count.ShouldBe(1);
        outcome.ShouldBeSameAs(results[0]);
        results[0].DisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_NextAttemptStarted_PreviousResultDisposedExactlyOnce()
    {
        var options = CreateDisposableOptions();
        options.MaxTotalRetryDuration = TimeSpan.FromHours(1);
        options.Delay = TimeSpan.FromMilliseconds(10);
        options.MaxRetryAttempts = 2;

        var results = new List<CountingDisposableResult>();
        var sut = CreateSut(options);

        var executing = sut.ExecuteAsync(_ =>
        {
            var result = new CountingDisposableResult();
            results.Add(result);
            return new ValueTask<CountingDisposableResult>(result);
        },
        TestCancellation.Token).AsTask();

        while (!executing.IsCompleted)
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        }

        var outcome = await executing;

        results.Count.ShouldBe(3);
        results[0].DisposeCount.ShouldBe(1);
        results[1].DisposeCount.ShouldBe(1);
        results[2].DisposeCount.ShouldBe(0);
        outcome.ShouldBeSameAs(results[2]);
    }

    [Fact]
    public void ExecuteAsync_BudgetExceeded_ReportsTelemetry()
    {
        _options.MaxTotalRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(20));
            return PredicateResult.True();
        };

        var sut = CreateSut(_options);

        sut.Execute(_ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
            return "result";
        },
        TestCancellation.Token);

        var args = _args.First(a => a.Event.EventName == RetryConstants.MaxTotalRetryDurationExceededEvent);
        args.Event.Severity.ShouldBe(ResilienceEventSeverity.Warning);
        var budgetArgs = args.Arguments.ShouldBeOfType<RetryBudgetExceededArguments>();
        budgetArgs.AttemptNumber.ShouldBe(0);
        budgetArgs.ElapsedTime.ShouldBe(TimeSpan.FromMilliseconds(60));
        budgetArgs.Budget.ShouldBe(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task ExecuteAsync_TotalBudget_DeterministicDemo()
    {
        // Deterministic 90-second demo of the total retry duration budget.
        // The fake time provider makes the whole run instantaneous and fully deterministic,
        // well within the 90 second wall-clock budget of a demo run.
        //
        // Timeline (budget = 100ms):
        // - 0ms: the original call starts (always allowed)
        // - 40ms: the original call ends with a handled result
        // - 60ms: ShouldHandle finishes (20ms)
        // - the planned 50ms delay does not fit into the remaining 40ms,
        //   so the strategy stops and returns the last result without replacing it
        _options.MaxTotalRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.MaxRetryAttempts = 10;
        _options.ShouldHandle = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(20));
            return PredicateResult.True();
        };

        var executions = 0;
        var sut = CreateSut(_options);

        var result = await sut.ExecuteAsync(_ =>
        {
            executions++;
            _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
            return new ValueTask<string>($"attempt-{executions}");
        },
        TestCancellation.Token);

        executions.ShouldBe(1);
        result.ShouldBe("attempt-1");
        _args.ShouldContain(a => a.Event.EventName == RetryConstants.MaxTotalRetryDurationExceededEvent);
    }

    private static RetryStrategyOptions<CountingDisposableResult> CreateDisposableOptions() => new()
    {
        MaxTotalRetryDuration = TimeSpan.FromMilliseconds(100),
        Delay = TimeSpan.FromMilliseconds(50),
        BackoffType = DelayBackoffType.Constant,
        ShouldHandle = _ => PredicateResult.True(),
    };

    private ResiliencePipeline<T> CreateSut<T>(RetryStrategyOptions<T> options) =>
        new RetryResilienceStrategy<T>(options, _timeProvider, _telemetry).AsPipeline();

    private sealed class CountingDisposableResult : IDisposable
    {
        public int DisposeCount { get; private set; }

        public bool IsDisposed => DisposeCount > 0;

        public void Dispose() => DisposeCount++;
    }
}
