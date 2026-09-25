using Microsoft.Extensions.Time.Testing;
using Polly.Retry;
using Polly.Telemetry;
using Polly.Testing;

namespace Polly.Core.Tests.Retry;

public class RetryResilienceStrategyTests
{
    private readonly RetryStrategyOptions _options = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<TelemetryEventArguments<object, object>> _args = [];
    private ResilienceStrategyTelemetry _telemetry;

    public RetryResilienceStrategyTests()
    {
        _telemetry = TestUtilities.CreateResilienceTelemetry(_args.Add);
        _options.ShouldHandle = _ => new ValueTask<bool>(false);
        _options.Randomizer = () => 1;
    }

    [Fact]
    public void ExecuteAsync_EnsureResultNotDisposed()
    {
        SetupNoDelay();
        var sut = CreateSut();

        var result = sut.Execute(() => new DisposableResult());
        result.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CanceledBeforeExecution_EnsureNotExecuted()
    {
        var sut = CreateSut();
        var executed = false;

        var result = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executed = true;
                return Outcome.FromResultAsValueTask(new object());
            },
            ResilienceContextPool.Shared.Get(new CancellationToken(canceled: true)),
            default(object));

        result.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        executed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CanceledDuringExecution_EnsureResultReturned()
    {
        var sut = CreateSut();
        using var cancellation = new CancellationTokenSource();
        var executions = 0;

        var result = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                cancellation.Cancel();
                return Outcome.FromResultAsValueTask(new object());
            },
            ResilienceContextPool.Shared.Get(cancellation.Token),
            default(object));

        result.Exception.ShouldBeNull();
        executions.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_ExceptionInExecution_EnsureResultReturned()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteOutcomeAsync<object, object?>((_, _) => throw new ArgumentException(), ResilienceContextPool.Shared.Get(TestCancellation.Token), default);
        result.Exception.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_CanceledDuringExecution_EnsureNotExecutedAgain()
    {
        var reported = false;

        _options.ShouldHandle = _ => PredicateResult.True();
        _options.OnRetry =
            args =>
            {
                reported = true;
                return default;
            };

        var sut = CreateSut();
        using var cancellation = new CancellationTokenSource();
        var executions = 0;

        var result = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                cancellation.Cancel();
                return Outcome.FromResultAsValueTask(new object());
            },
            ResilienceContextPool.Shared.Get(cancellation.Token),
            default(object));

        result.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        executions.ShouldBe(1);
        reported.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_CanceledAfterExecution_EnsureNotExecutedAgain()
    {
        using var cancellation = new CancellationTokenSource();

        _options.ShouldHandle = _ => PredicateResult.True();
        _options.OnRetry =
            args =>
            {
                cancellation.Cancel();
                return default;
            };

        var sut = CreateSut();
        var executions = 0;

        var result = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                return Outcome.FromResultAsValueTask(new object());
            },
            ResilienceContextPool.Shared.Get(cancellation.Token),
            default(object));

        result.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        executions.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_CanceledDuringDelay_EnsureNotExecutedAgain()
    {
        _options.ShouldHandle = _ => PredicateResult.True();

        using var cancellation = _timeProvider.CreateCancellationTokenSource(_options.Delay);

        var sut = CreateSut();
        var executions = 0;

        var resultTask = sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                return Outcome.FromResultAsValueTask(new object());
            },
            ResilienceContextPool.Shared.Get(cancellation.Token),
            default(object));

        _timeProvider.Advance(_options.Delay);
        var result = await resultTask;

        result.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        executions.ShouldBe(1);
    }

    [Fact]
    public void ExecuteAsync_MultipleRetries_EnsureDiscardedResultsDisposed()
    {
        // arrange
        _options.MaxRetryAttempts = 5;
        SetupNoDelay();
        _options.ShouldHandle = _ => PredicateResult.True();
        var results = new List<DisposableResult>();
        var sut = CreateSut();

        // act
        var result = sut.Execute(_ =>
        {
            var r = new DisposableResult();
            results.Add(r);
            return r;
        }, TestCancellation.Token);

        // assert
        result.IsDisposed.ShouldBeFalse();
        results.Count.ShouldBe(_options.MaxRetryAttempts + 1);
        results[results.Count - 1].IsDisposed.ShouldBeFalse();

        results.Remove(results[results.Count - 1]);
        results.ShouldAllBe(r => r.IsDisposed);
    }

    [Fact]
    public void Retry_RetryCount_Respected()
    {
        int calls = 0;
        _options.OnRetry = _ => { calls++; return default; };
        _options.ShouldHandle = args => args.Outcome.ResultPredicateAsync(0);
        _options.MaxRetryAttempts = 12;
        SetupNoDelay();
        var sut = CreateSut();

        sut.Execute(() => 0);

        calls.ShouldBe(12);
    }

    [Fact]
    public void RetryException_RetryCount_Respected()
    {
        int calls = 0;
        _options.OnRetry = args =>
        {
            args.Outcome.Exception.ShouldBeOfType<InvalidOperationException>();
            calls++;
            return default;
        };

        _options.ShouldHandle = args => args.Outcome.ExceptionPredicateAsync<InvalidOperationException>();
        _options.MaxRetryAttempts = 3;
        SetupNoDelay();
        var sut = CreateSut();

        Assert.Throws<InvalidOperationException>(() => sut.Execute<int>(() => throw new InvalidOperationException()));

        calls.ShouldBe(3);
    }

    [Fact]
    public void RetryDelayGenerator_Respected()
    {
        var retries = 0;
        var generatedValues = 0;
        var delay = TimeSpan.FromMilliseconds(120);

        _options.ShouldHandle = _ => PredicateResult.True();
        _options.MaxRetryAttempts = 3;
        _options.BackoffType = DelayBackoffType.Constant;

        _options.OnRetry = args =>
        {
            retries++;
            args.RetryDelay.ShouldBe(delay);
            return default;
        };
        _options.DelayGenerator = _ =>
        {
            generatedValues++;
            return new ValueTask<TimeSpan?>(delay);
        };

        CreateSut(TimeProvider.System).Execute(_ => "dummy", TestCancellation.Token);

        retries.ShouldBe(3);
        generatedValues.ShouldBe(3);
    }

    [Fact]
    public async Task RetryDelayGenerator_ZeroDelay_NoTimeProviderCalls()
    {
        int retries = 0;
        int generatedValues = 0;

        var delay = TimeSpan.Zero;
        var provider = new FakeTimeProvider();

        _options.ShouldHandle = _ => PredicateResult.True();
        _options.MaxRetryAttempts = 3;
        _options.BackoffType = DelayBackoffType.Constant;

        _options.OnRetry = _ =>
        {
            retries++;
            return default;
        };
        _options.DelayGenerator = _ =>
        {
            generatedValues++;
            return new ValueTask<TimeSpan?>(delay);
        };

        var sut = CreateSut(provider);
        await sut.ExecuteAsync(_ => new ValueTask<string>("dummy"), TestCancellation.Token);

        retries.ShouldBe(3);
        generatedValues.ShouldBe(3);
    }

    [Fact]
    public void IsLastAttempt_Ok()
    {
        var sut = (RetryResilienceStrategy<object>)CreateSut().GetPipelineDescriptor().FirstStrategy.StrategyInstance;

        sut.IsLastAttempt(int.MaxValue, out var increment).ShouldBeFalse();
        increment.ShouldBeFalse();
    }

    [Fact]
    public async Task OnRetry_EnsureCorrectArguments()
    {
        var attempts = new List<int>();
        var delays = new List<TimeSpan>();
        _options.OnRetry = args =>
        {
            attempts.Add(args.AttemptNumber);
            delays.Add(args.RetryDelay);

            args.Outcome.Exception.ShouldBeNull();
            args.Outcome.Result.ShouldBe(0);
            return default;
        };

        _options.ShouldHandle = args => PredicateResult.True();
        _options.MaxRetryAttempts = 3;
        _options.BackoffType = DelayBackoffType.Linear;

        var sut = CreateSut();

        var executing = ExecuteAndAdvance(sut);

        await executing;

        attempts.Count.ShouldBe(3);
        attempts[0].ShouldBe(0);
        attempts[1].ShouldBe(1);
        attempts[2].ShouldBe(2);

        delays[0].ShouldBe(TimeSpan.FromSeconds(2));
        delays[1].ShouldBe(TimeSpan.FromSeconds(4));
        delays[2].ShouldBe(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task MaxDelay_EnsureRespected()
    {
        var delays = new List<TimeSpan>();
        _options.OnRetry = args =>
        {
            delays.Add(args.RetryDelay);
            return default;
        };

        _options.ShouldHandle = args => PredicateResult.True();
        _options.MaxRetryAttempts = 3;
        _options.BackoffType = DelayBackoffType.Linear;
        _options.MaxDelay = TimeSpan.FromMilliseconds(123);

        var sut = CreateSut();

        await ExecuteAndAdvance(sut);

        delays[0].ShouldBe(TimeSpan.FromMilliseconds(123));
        delays[1].ShouldBe(TimeSpan.FromMilliseconds(123));
        delays[2].ShouldBe(TimeSpan.FromMilliseconds(123));
    }

    [Fact]
    public async Task OnRetry_EnsureExecutionTime()
    {
        _options.OnRetry = args =>
        {
            args.Duration.ShouldBe(TimeSpan.FromMinutes(1));

            return default;
        };

        _options.ShouldHandle = _ => PredicateResult.True();
        _options.MaxRetryAttempts = 1;
        _options.BackoffType = DelayBackoffType.Constant;
        _options.Delay = TimeSpan.Zero;

        var sut = CreateSut();

        await sut.ExecuteAsync(_ =>
        {
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
            return new ValueTask<int>(0);
        }, TestCancellation.Token).AsTask();
    }

    [Fact]
    public void Execute_NotHandledOriginalAttempt_EnsureAttemptReported()
    {
        var called = false;
        _telemetry = TestUtilities.CreateResilienceTelemetry(args =>
        {
            var attempt = args.Arguments.ShouldBeOfType<ExecutionAttemptArguments>();
            args.Event.Severity.ShouldBe(ResilienceEventSeverity.Information);
            attempt.Handled.ShouldBeFalse();
            attempt.AttemptNumber.ShouldBe(0);
            attempt.Duration.ShouldBe(TimeSpan.FromSeconds(1));
            called = true;
        });

        var sut = CreateSut();

        sut.Execute(() =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            return 0;
        });

        called.ShouldBeTrue();
    }

    [Fact]
    public void Execute_NotHandledFinalAttempt_EnsureAttemptReported()
    {
        _options.MaxRetryAttempts = 1;
        _options.Delay = TimeSpan.Zero;

        // original attempt is handled, retried attempt is not handled
        _options.ShouldHandle = args => new ValueTask<bool>(args.AttemptNumber == 0);
        var called = false;
        _telemetry = TestUtilities.CreateResilienceTelemetry(args =>
        {
            // ignore OnRetry event
            if (args.Arguments is OnRetryArguments<object>)
            {
                return;
            }

            var attempt = args.Arguments.ShouldBeOfType<ExecutionAttemptArguments>();
            if (attempt.AttemptNumber == 0)
            {
                args.Event.Severity.ShouldBe(ResilienceEventSeverity.Warning);
            }
            else
            {
                args.Event.Severity.ShouldBe(ResilienceEventSeverity.Information);
            }

            called = true;
        });

        var sut = CreateSut();

        sut.Execute(() =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            return 0;
        });

        called.ShouldBeTrue();
    }

    [Fact]
    public void Execute_HandledFinalAttempt_EnsureAttemptReported()
    {
        _options.MaxRetryAttempts = 1;
        _options.Delay = TimeSpan.Zero;
        _options.ShouldHandle = _ => new ValueTask<bool>(true);
        var called = false;
        _telemetry = TestUtilities.CreateResilienceTelemetry(args =>
        {
            // ignore OnRetry event
            if (args.Arguments is OnRetryArguments<object>)
            {
                return;
            }

            var attempt = args.Arguments.ShouldBeOfType<ExecutionAttemptArguments>();
            if (attempt.AttemptNumber == 0)
            {
                args.Event.Severity.ShouldBe(ResilienceEventSeverity.Warning);
            }
            else
            {
                args.Event.Severity.ShouldBe(ResilienceEventSeverity.Error);
            }

            called = true;
        });

        var sut = CreateSut();

        sut.Execute(() =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            return 0;
        });

        called.ShouldBeTrue();
    }

    [Fact]
    public async Task OnRetry_EnsureTelemetry()
    {
        var attempts = new List<int>();
        var delays = new List<TimeSpan>();

        _options.ShouldHandle = args => args.Outcome.ResultPredicateAsync(0);
        _options.MaxRetryAttempts = 3;
        _options.BackoffType = DelayBackoffType.Linear;

        var sut = CreateSut();

        await ExecuteAndAdvance(sut);

        _args.Select(a => a.Arguments).OfType<OnRetryArguments<object>>().Count().ShouldBe(3);
    }

    [Fact]
    public void RetryDelayGenerator_EnsureCorrectArguments()
    {
        var attempts = new List<int>();
        var hints = new List<TimeSpan>();
        _options.DelayGenerator = args =>
        {
            attempts.Add(args.AttemptNumber);

            args.Outcome.Exception.ShouldBeNull();
            args.Outcome.Result.ShouldBe(0);

            return new ValueTask<TimeSpan?>(TimeSpan.Zero);
        };

        _options.ShouldHandle = args => args.Outcome.ResultPredicateAsync(0);
        _options.MaxRetryAttempts = 3;
        _options.BackoffType = DelayBackoffType.Linear;

        var sut = CreateSut();

        sut.Execute(() => 0);

        attempts.Count.ShouldBe(3);
        attempts[0].ShouldBe(0);
        attempts[1].ShouldBe(1);
        attempts[2].ShouldBe(2);
    }

    [Fact]
    public void RetryDelayGenerator_ReturnsNull_EnsureDefaultRetry()
    {
        var delays = new List<TimeSpan>();
        _options.DelayGenerator = args => new ValueTask<TimeSpan?>((TimeSpan?)null);
        _options.OnRetry = args =>
        {
            delays.Add(args.RetryDelay);
            return default;
        };
        _options.ShouldHandle = args => args.Outcome.ResultPredicateAsync(0);
        _options.MaxRetryAttempts = 2;
        _options.BackoffType = DelayBackoffType.Constant;
        _options.Delay = TimeSpan.FromMilliseconds(2);

        var sut = CreateSut(TimeProvider.System);

        sut.Execute(() => 0);

        delays.Count.ShouldBe(2);
        delays[0].ShouldBe(TimeSpan.FromMilliseconds(2));
        delays[1].ShouldBe(TimeSpan.FromMilliseconds(2));
    }

    [Fact]
    public async Task MaxRetryDuration_InsufficientBudgetForNextDelay_StopsBeforeSecondAttempt()
    {
        // Deterministic demo (finishes in a few milliseconds thanks to the fake clock, well under 90 seconds):
        // budget is 100ms, the original call takes 40ms, the strategy callbacks take 20ms and the planned
        // delay is 50ms. The remaining 40ms cannot accommodate the whole 50ms delay, so the second attempt
        // never starts and the last result is returned.
        _options.MaxRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ => PredicateResult.True();
        _options.MaxRetryAttempts = 10;
        _options.OnRetry = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(20));
            return default;
        };

        var sut = CreateSut();
        var executions = 0;

        var result = await sut.ExecuteAsync(
            _ =>
            {
                executions++;
                _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
                return new ValueTask<int>(0);
            },
            TestCancellation.Token);

        executions.ShouldBe(1);
        result.ShouldBe(0);
    }

    [Fact]
    public async Task MaxRetryDuration_RemainingBudgetEqualsDelay_WaitsButDoesNotStartNextAttempt()
    {
        // Budget is 100ms. After the original call (40ms) and the callbacks (10ms), the remaining budget (50ms)
        // exactly equals the planned delay (50ms), so the wait is allowed. When the strategy wakes up the budget
        // is exhausted, so the next attempt is not started.
        _options.MaxRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ => PredicateResult.True();
        _options.MaxRetryAttempts = 10;
        _options.OnRetry = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return default;
        };

        var sut = CreateSut();
        var executions = 0;

        var executing = sut.ExecuteAsync(
            _ =>
            {
                executions++;
                _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
                return new ValueTask<int>(0);
            },
            TestCancellation.Token).AsTask();

        // the strategy is waiting for the full, non-truncated 50ms delay
        executing.IsCompleted.ShouldBeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(50));

        var result = await executing;

        executions.ShouldBe(1);
        result.ShouldBe(0);
    }

    [Fact]
    public async Task MaxRetryDuration_AllStagesContributeToElapsedTime()
    {
        // Budget is 100ms. Each stage consumes time: original call 30ms, ShouldHandle 10ms,
        // DelayGenerator 10ms and OnRetry 10ms (60ms in total). The remaining 40ms cannot
        // accommodate the planned 50ms delay, so the second attempt never starts.
        _options.MaxRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.MaxRetryAttempts = 10;
        _options.ShouldHandle = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return PredicateResult.True();
        };
        _options.DelayGenerator = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        };
        _options.OnRetry = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return default;
        };

        var sut = CreateSut();
        var executions = 0;

        await sut.ExecuteAsync(
            _ =>
            {
                executions++;
                _timeProvider.Advance(TimeSpan.FromMilliseconds(30));
                return new ValueTask<int>(0);
            },
            TestCancellation.Token);

        executions.ShouldBe(1);
    }

    [Fact]
    public void MaxRetryDuration_BudgetExhausted_EnsureTelemetryReported()
    {
        _options.MaxRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ => PredicateResult.True();
        _options.OnRetry = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(60));
            return default;
        };

        var sut = CreateSut();

        sut.Execute(() => 0);

        var args = _args.Single(a => a.Event.EventName == RetryConstants.BudgetExhaustedEvent);
        args.Event.Severity.ShouldBe(ResilienceEventSeverity.Warning);
        args.Arguments.ShouldBeOfType<OnRetryArguments<object>>().RetryDelay.ShouldBe(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void MaxRetryDuration_AttemptsExhaustedFirst_EnsureNoBudgetTelemetry()
    {
        // MaxRetryAttempts takes precedence over the budget: when the attempts are exhausted
        // the last outcome is returned without reporting a budget-related event.
        _options.MaxRetryDuration = TimeSpan.FromHours(1);
        _options.MaxRetryAttempts = 1;
        _options.ShouldHandle = _ => PredicateResult.True();
        SetupNoDelay();

        var sut = CreateSut();
        var executions = 0;

        sut.Execute(() =>
        {
            executions++;
            return 0;
        });

        executions.ShouldBe(2);
        _args.ShouldNotContain(a => a.Event.EventName == RetryConstants.BudgetExhaustedEvent);
    }

    [Fact]
    public void MaxRetryDuration_BudgetExhausted_EnsureLastResultNotDisposed()
    {
        _options.MaxRetryDuration = TimeSpan.FromMilliseconds(100);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ => PredicateResult.True();
        _options.OnRetry = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(60));
            return default;
        };

        var sut = CreateSut();
        var results = new List<DisposableResult>();

        var result = sut.Execute(() =>
        {
            var r = new DisposableResult();
            results.Add(r);
            _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
            return r;
        });

        results.Count.ShouldBe(1);
        result.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task MaxRetryDuration_IntermediateResultsDisposed_LastResultReturnedIntact()
    {
        // Budget is 150ms. Each attempt consumes 40ms for the call and 10ms for OnRetry, the delay is 50ms.
        // First attempt: 50ms elapsed, the remaining 100ms fits the 50ms delay. After the wait 100ms < 150ms,
        // so the second attempt starts and the first result is disposed.
        // Second attempt: 150ms elapsed, no budget left for the next 50ms delay. The last result is returned undisposed.
        _options.MaxRetryDuration = TimeSpan.FromMilliseconds(150);
        _options.Delay = TimeSpan.FromMilliseconds(50);
        _options.ShouldHandle = _ => PredicateResult.True();
        _options.MaxRetryAttempts = 10;
        _options.OnRetry = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            return default;
        };

        var sut = CreateSut();
        var results = new List<DisposableResult>();

        var executing = sut.ExecuteAsync(
            _ =>
            {
                var r = new DisposableResult();
                results.Add(r);
                _timeProvider.Advance(TimeSpan.FromMilliseconds(40));
                return new ValueTask<DisposableResult>(r);
            },
            TestCancellation.Token).AsTask();

        while (!executing.IsCompleted)
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(50));
        }

        var result = await executing;

        results.Count.ShouldBe(2);
        results[0].IsDisposed.ShouldBeTrue();
        result.ShouldBeSameAs(results[1]);
        result.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task MaxRetryDuration_CanceledDuringDelay_EnsureCancellationRespected()
    {
        _options.MaxRetryDuration = TimeSpan.FromMinutes(10);
        _options.ShouldHandle = _ => PredicateResult.True();

        using var cancellation = _timeProvider.CreateCancellationTokenSource(_options.Delay);

        var sut = CreateSut();
        var executions = 0;

        var resultTask = sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                return Outcome.FromResultAsValueTask(new object());
            },
            ResilienceContextPool.Shared.Get(cancellation.Token),
            default(object));

        _timeProvider.Advance(_options.Delay);
        var result = await resultTask;

        result.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        executions.ShouldBe(1);
    }

    private void SetupNoDelay() => _options.DelayGenerator = _ => new ValueTask<TimeSpan?>(TimeSpan.Zero);

    private async ValueTask<int> ExecuteAndAdvance(ResiliencePipeline<object> sut)
    {
        var executing = sut.ExecuteAsync(_ => new ValueTask<int>(0)).AsTask();

        while (!executing.IsCompleted)
        {
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
        }

        return await executing;
    }

    private ResiliencePipeline<object> CreateSut(TimeProvider? timeProvider = null) =>
        new RetryResilienceStrategy<object>(_options, timeProvider ?? _timeProvider, _telemetry).AsPipeline();
}
