using Microsoft.Extensions.Time.Testing;
using Polly.Core.Tests.Helpers;
using Polly.Retry;

namespace Polly.Core.Tests.Retry;

public class RetryCleanupResultTests
{
    private readonly List<string> _events = [];
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly RetryStrategyOptions<CountedResource> _options = new();

    public RetryCleanupResultTests()
    {
        _options.MaxRetryAttempts = 2;
        _options.Delay = TimeSpan.FromSeconds(10);
        _options.BackoffType = DelayBackoffType.Constant;
        _options.ShouldHandle = _ => PredicateResult.True();
        _options.Randomizer = () => 1;
    }

    [Fact]
    public async Task CleanupResult_TwoRetriesThenSuccess_DiscardedResourcesCleanedOnceFinalPreserved()
    {
        // arrange: first two attempts produce handled results, the third one succeeds
        var resources = new List<CountedResource>();
        var attempts = 0;
        _options.ShouldHandle = args => new ValueTask<bool>(args.Outcome.Result!.Id < 2);
        _options.OnRetry = args =>
        {
            _events.Add($"on-retry:{args.AttemptNumber}");
            return default;
        };
        _options.CleanupResult = args =>
        {
            _events.Add($"cleanup:{args.AttemptNumber}");
            args.Outcome.Result!.Release();
            return default;
        };
        _options.DelayGenerator = args =>
        {
            // the delay value is calculated before the notification; it is awaited after the cleanup
            _events.Add($"delay:{args.AttemptNumber}");
            return new ValueTask<TimeSpan?>(TimeSpan.Zero);
        };

        var sut = CreateSut();

        // act
        var final = await sut.ExecuteAsync(_ =>
        {
            var resource = new CountedResource(attempts);
            resources.Add(resource);
            _events.Add($"execute:{attempts}");
            attempts++;
            return new ValueTask<CountedResource>(resource);
        }, TestCancellation.Token);

        // assert
        ReferenceEquals(final, resources[2]).ShouldBeTrue();
        resources.Count.ShouldBe(3);
        resources[0].CleanupCount.ShouldBe(1);
        resources[1].CleanupCount.ShouldBe(1);
        resources[2].CleanupCount.ShouldBe(0);
        final.CleanupCount.ShouldBe(0);

        _events.ShouldBe(
        [
            "execute:0",
            "delay:0",
            "on-retry:0",
            "cleanup:0",
            "execute:1",
            "delay:1",
            "on-retry:1",
            "cleanup:1",
            "execute:2",
        ]);
    }

    [Fact]
    public async Task CleanupResult_OnRetryThrows_StillCleanedUpAndRetryStopped()
    {
        // arrange
        var notificationError = new InvalidOperationException("on-retry failed");
        var executions = 0;
        _options.OnRetry = _ => throw notificationError;
        _options.CleanupResult = args =>
        {
            _events.Add($"cleanup:{args.AttemptNumber}");
            args.Outcome.Result!.Release();
            return default;
        };
        SetupNoDelay();

        var sut = CreateSut();

        // act
        var outcome = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                _events.Add($"execute:{executions}");
                return Outcome.FromResultAsValueTask(new CountedResource(executions++));
            },
            ResilienceContextPool.Shared.Get(TestCancellation.Token),
            0);

        // assert
        executions.ShouldBe(1);
        outcome.Exception.ShouldBe(notificationError);
        outcome.Result.ShouldBeNull();
        _events.ShouldBe(
        [
            "execute:0",
            "cleanup:0",
        ]);
    }

    [Fact]
    public async Task CleanupResult_CanceledDuringOnRetry_StillCleanedUpAndCancelationReported()
    {
        // arrange
        using var cancellation = new CancellationTokenSource();
        var executions = 0;
        _options.OnRetry = _ =>
        {
            _events.Add("on-retry");
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };
        _options.CleanupResult = args =>
        {
            _events.Add("cleanup");
            args.Outcome.Result!.Release();
            return default;
        };
        SetupNoDelay();

        var sut = CreateSut();

        // act
        var outcome = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                _events.Add($"execute:{executions}");
                return Outcome.FromResultAsValueTask(new CountedResource(executions++));
            },
            ResilienceContextPool.Shared.Get(cancellation.Token),
            0);

        // assert
        executions.ShouldBe(1);
        outcome.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        _events.ShouldBe(
        [
            "execute:0",
            "on-retry",
            "cleanup",
        ]);
    }

    [Fact]
    public async Task CleanupResult_CanceledDuringDelay_CleanupAlreadyDoneAndRetryStopped()
    {
        // arrange: the cancellation is triggered by the controllable time provider while the delay is being awaited
        using var cancellation = _timeProvider.CreateCancellationTokenSource(_options.Delay);
        var executions = 0;
        _options.CleanupResult = args =>
        {
            _events.Add($"cleanup:{args.AttemptNumber}");
            args.Outcome.Result!.Release();
            return default;
        };

        var sut = CreateSut();

        // act
        var execution = sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                _events.Add($"execute:{executions}");
                return Outcome.FromResultAsValueTask(new CountedResource(executions++));
            },
            ResilienceContextPool.Shared.Get(cancellation.Token),
            0);

        var outcome = await AdvanceAndGetAsync(execution);

        // assert
        executions.ShouldBe(1);
        outcome.Exception.ShouldBeAssignableTo<OperationCanceledException>();
        _events.ShouldBe(
        [
            "execute:0",
            "cleanup:0",
        ]);
    }

    [Fact]
    public async Task CleanupResult_CleanupFails_OriginalAndCleanupErrorsCombinedAndRetryStopped()
    {
        // arrange: the discarded outcome carries the original execution exception while cleanup also fails
        var originalException = new InvalidOperationException("execution failed");
        var cleanupException = new IOException("cleanup failed");
        var executions = 0;
        _options.CleanupResult = _ => throw cleanupException;
        SetupNoDelay();

        var sut = CreateSut();

        // act
        var outcome = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                return Outcome.FromExceptionAsValueTask<CountedResource>(originalException);
            },
            ResilienceContextPool.Shared.Get(TestCancellation.Token),
            0);

        // assert
        executions.ShouldBe(1);
        var aggregate = outcome.Exception.ShouldBeOfType<AggregateException>();
        aggregate.InnerExceptions.Count.ShouldBe(2);
        aggregate.InnerExceptions[0].ShouldBe(cleanupException);
        aggregate.InnerExceptions[1].ShouldBe(originalException);
    }

    [Fact]
    public async Task CleanupResult_CleanupFailsWithResultOutcome_OnlyCleanupErrorReported()
    {
        // arrange
        var cleanupException = new IOException("cleanup failed");
        var resource = new CountedResource(0);
        var executions = 0;
        _options.CleanupResult = _ => throw cleanupException;
        SetupNoDelay();

        var sut = CreateSut();

        // act
        var outcome = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                return Outcome.FromResultAsValueTask(resource);
            },
            ResilienceContextPool.Shared.Get(TestCancellation.Token),
            0);

        // assert
        executions.ShouldBe(1);
        outcome.Exception.ShouldBe(cleanupException);
        resource.CleanupCount.ShouldBe(0);
    }

    [Fact]
    public async Task CleanupResult_NotificationAndCleanupBothFail_NotificationErrorPreservedAfterCleanupError()
    {
        // arrange
        var notificationException = new InvalidOperationException("on-retry failed");
        var cleanupException = new IOException("cleanup failed");
        var executions = 0;
        _options.OnRetry = _ => throw notificationException;
        _options.CleanupResult = _ => throw cleanupException;
        SetupNoDelay();

        var sut = CreateSut();

        // act
        var outcome = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                executions++;
                return Outcome.FromResultAsValueTask(new CountedResource(0));
            },
            ResilienceContextPool.Shared.Get(TestCancellation.Token),
            0);

        // assert
        executions.ShouldBe(1);
        var aggregate = outcome.Exception.ShouldBeOfType<AggregateException>();
        aggregate.InnerExceptions.Count.ShouldBe(2);
        aggregate.InnerExceptions[0].ShouldBe(cleanupException);
        aggregate.InnerExceptions[1].ShouldBe(notificationException);
    }

    [Fact]
    public async Task CleanupResult_LastAttemptStillFails_FinalOutcomeReturnedAndNotCleaned()
    {
        // arrange: MaxRetryAttempts is 2, so the third failed attempt is returned without being cleaned up
        var finalException = new InvalidOperationException("final");
        var executions = 0;
        SetupNoDelay();
        _options.CleanupResult = args =>
        {
            _events.Add($"cleanup:{args.AttemptNumber}");
            return default;
        };

        var sut = CreateSut();

        // act
        var outcome = await sut.ExecuteOutcomeAsync(
            (_, _) =>
            {
                var exception = executions == 2 ? finalException : new InvalidOperationException($"attempt {executions}");
                _events.Add($"execute:{executions}");
                executions++;
                return Outcome.FromExceptionAsValueTask<CountedResource>(exception);
            },
            ResilienceContextPool.Shared.Get(TestCancellation.Token),
            0);

        // assert
        executions.ShouldBe(3);
        outcome.Exception.ShouldBe(finalException);
        _events.ShouldBe(
        [
            "execute:0",
            "cleanup:0",
            "execute:1",
            "cleanup:1",
            "execute:2",
        ]);
    }

    [Fact]
    public void CleanupResult_SynchronousExecution_CleanupRunsSynchronouslyAndFinalResultPreserved()
    {
        // arrange
        var resources = new List<CountedResource>();
        var attempts = 0;
        _options.ShouldHandle = args => new ValueTask<bool>(args.Outcome.Result!.Id < 2);
        _options.CleanupResult = args =>
        {
            _events.Add($"cleanup:{args.AttemptNumber}");
            args.Outcome.Result!.Release();
            return default;
        };
        SetupNoDelay();

        var sut = CreateSut();

        // act
        var final = sut.Execute(_ =>
        {
            var resource = new CountedResource(attempts);
            resources.Add(resource);
            _events.Add($"execute:{attempts}");
            attempts++;
            return resource;
        }, TestCancellation.Token);

        // assert
        resources.Count.ShouldBe(3);
        resources[0].CleanupCount.ShouldBe(1);
        resources[1].CleanupCount.ShouldBe(1);
        resources[2].CleanupCount.ShouldBe(0);
        final.CleanupCount.ShouldBe(0);
        _events.ShouldBe(
        [
            "execute:0",
            "cleanup:0",
            "execute:1",
            "cleanup:1",
            "execute:2",
        ]);
    }

    [Fact]
    public async Task CleanupResult_AsynchronousCleanup_AwaitedBeforeNextAttempt()
    {
        // arrange
        var resources = new List<CountedResource>();
        var attempts = 0;
        _options.ShouldHandle = args => new ValueTask<bool>(args.Outcome.Result!.Id < 1);
        var cleanupStarted = false;
        _options.CleanupResult = async args =>
        {
            cleanupStarted = true;
            _events.Add($"cleanup-begin:{args.AttemptNumber}");
            await Task.Yield();
            args.Outcome.Result!.Release();
            _events.Add($"cleanup-end:{args.AttemptNumber}");
        };
        SetupNoDelay();

        var sut = CreateSut();

        // act
        var final = await sut.ExecuteAsync(_ =>
        {
            var resource = new CountedResource(attempts);
            resources.Add(resource);
            _events.Add($"execute:{attempts}");
            attempts++;
            return new ValueTask<CountedResource>(resource);
        }, TestCancellation.Token);

        // assert
        cleanupStarted.ShouldBeTrue();
        resources[0].CleanupCount.ShouldBe(1);
        resources[1].CleanupCount.ShouldBe(0);
        final.CleanupCount.ShouldBe(0);
        _events.ShouldBe(
        [
            "execute:0",
            "cleanup-begin:0",
            "cleanup-end:0",
            "execute:1",
        ]);
    }

    [Fact]
    public async Task CleanupResult_NotConfigured_DefaultDisposalBehaviorRemains()
    {
        // arrange: without CleanupResult the built-in best-effort disposal still runs and the final result is preserved
        SetupNoDelay();
        var results = new List<DisposableResult>();
        var options = new RetryStrategyOptions<DisposableResult>
        {
            MaxRetryAttempts = 2,
            ShouldHandle = _ => PredicateResult.True(),
            DelayGenerator = _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
            Randomizer = () => 1,
        };

        var sut = new RetryResilienceStrategy<DisposableResult>(
            options,
            _timeProvider,
            TestUtilities.CreateResilienceTelemetry(_ => { })).AsPipeline();

        // act
        var final = await sut.ExecuteAsync(_ =>
        {
            var result = new DisposableResult();
            results.Add(result);
            return new ValueTask<DisposableResult>(result);
        }, TestCancellation.Token);

        // assert
        results.Count.ShouldBe(3);
        results[0].IsDisposed.ShouldBeTrue();
        results[1].IsDisposed.ShouldBeTrue();
        results[2].IsDisposed.ShouldBeFalse();
        final.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public void CleanupResultArguments_Ctor_Ok()
    {
        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var outcome = Outcome.FromResult(new CountedResource(7));

        var args = new CleanupResultArguments<CountedResource>(context, outcome, 3);

        args.Context.ShouldBe(context);
        args.Outcome.ShouldBe(outcome);
        args.AttemptNumber.ShouldBe(3);
    }

    private void SetupNoDelay() => _options.DelayGenerator = _ => new ValueTask<TimeSpan?>(TimeSpan.Zero);

    private async Task<Outcome<CountedResource>> AdvanceAndGetAsync(ValueTask<Outcome<CountedResource>> execution)
    {
        while (!execution.IsCompleted)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        return await execution;
    }

    private ResiliencePipeline<CountedResource> CreateSut() =>
        new RetryResilienceStrategy<CountedResource>(
            _options,
            _timeProvider,
            TestUtilities.CreateResilienceTelemetry(_ => { })).AsPipeline();
}
