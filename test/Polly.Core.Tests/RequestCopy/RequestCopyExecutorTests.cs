using Microsoft.Extensions.Time.Testing;
using Polly.Retry;

namespace Polly.Core.Tests.RequestCopy;

public class RequestCopyExecutorTests
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task ExecuteAsync_FirstFailsSecondSucceeds_TwoCopiesCreatedAndReleased()
    {
        var factory = new TestRequestCopyFactory();
        var options = new RequestCopyResilienceStrategyOptions<TestRequest>
        {
            Factory = factory,
            Ownership = RequestCopyOwnership.AdapterOwned,
        };

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = _ => PredicateResult.True(),
            })
            .AddRequestCopy(options)
            .Build();

        var executor = new RequestCopyExecutor<TestRequest>(pipeline, factory);
        var originalRequest = new TestRequest { Id = "original" };
        var attemptCount = 0;
        var copies = new List<TestRequest>();

        var result = await executor.ExecuteAsync<string>(
            originalRequest,
            (_, request, _) =>
            {
                attemptCount++;
                copies.Add(request);
                if (attemptCount == 1)
                {
                    throw new InvalidOperationException("First attempt failed");
                }
                return new ValueTask<string>("success");
            },
            TestCancellation.Token);

        result.ShouldBe("success");
        attemptCount.ShouldBe(2);
        factory.CreateCopyCount.ShouldBe(2);
        factory.ReleaseCopyCount.ShouldBe(2);
        copies.Count.ShouldBe(2);
        copies[0].ShouldNotBeSameAs(originalRequest);
        copies[1].ShouldNotBeSameAs(originalRequest);
        copies[0].ShouldNotBeSameAs(copies[1]);
        originalRequest.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_SecondFactoryThrows_CallbackNotCalled()
    {
        var factory = new ThrowingRequestCopyFactory();
        var options = new RequestCopyResilienceStrategyOptions<TestRequest>
        {
            Factory = factory,
            Ownership = RequestCopyOwnership.AdapterOwned,
        };

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
            })
            .AddRequestCopy(options)
            .Build();

        var executor = new RequestCopyExecutor<TestRequest>(pipeline, factory);
        var originalRequest = new TestRequest { Id = "original" };
        var callbackCount = 0;

        var exception = await Should.ThrowAsync<OperationCanceledException>(
            () => executor.ExecuteAsync<string>(
                originalRequest,
                (_, _, _) =>
                {
                    callbackCount++;
                    return new ValueTask<string>("success");
                },
                TestCancellation.Token).AsTask());

        callbackCount.ShouldBe(0);
        factory.CreateCopyCount.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringRetryWait_NoThirdCopyCreated()
    {
        var factory = new TestRequestCopyFactory();
        var options = new RequestCopyResilienceStrategyOptions<TestRequest>
        {
            Factory = factory,
            Ownership = RequestCopyOwnership.AdapterOwned,
        };

        using var cts = new CancellationTokenSource();
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = _timeProvider }
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = _ => PredicateResult.True(),
                OnRetry = _ =>
                {
                    cts.Cancel();
                    return default;
                },
            })
            .AddRequestCopy(options)
            .Build();

        var executor = new RequestCopyExecutor<TestRequest>(pipeline, factory);
        var originalRequest = new TestRequest { Id = "original" };
        var attemptCount = 0;

        var executing = executor.ExecuteAsync<string>(
            originalRequest,
            (_, _, _) =>
            {
                attemptCount++;
                throw new InvalidOperationException("attempt failed");
            },
            cts.Token).AsTask();

        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => executing);

        attemptCount.ShouldBe(1);
        factory.CreateCopyCount.ShouldBe(1);
        factory.ReleaseCopyCount.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_CallerOwned_CopyNotReleased()
    {
        var factory = new TestRequestCopyFactory();
        var options = new RequestCopyResilienceStrategyOptions<TestRequest>
        {
            Factory = factory,
            Ownership = RequestCopyOwnership.CallerOwned,
        };

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = _ => PredicateResult.False(),
            })
            .AddRequestCopy(options)
            .Build();

        var executor = new RequestCopyExecutor<TestRequest>(pipeline, factory);
        var originalRequest = new TestRequest { Id = "original" };

        var result = await executor.ExecuteAsync<string>(
            originalRequest,
            (_, _, _) => new ValueTask<string>("success"),
            TestCancellation.Token);

        result.ShouldBe("success");
        factory.CreateCopyCount.ShouldBe(1);
        factory.ReleaseCopyCount.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_AdapterOwned_CopyReleasedAfterAttempt()
    {
        var factory = new TestRequestCopyFactory();
        var options = new RequestCopyResilienceStrategyOptions<TestRequest>
        {
            Factory = factory,
            Ownership = RequestCopyOwnership.AdapterOwned,
        };

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = _ => PredicateResult.False(),
            })
            .AddRequestCopy(options)
            .Build();

        var executor = new RequestCopyExecutor<TestRequest>(pipeline, factory);
        var originalRequest = new TestRequest { Id = "original" };

        var result = await executor.ExecuteAsync<string>(
            originalRequest,
            (_, _, _) => new ValueTask<string>("success"),
            TestCancellation.Token);

        result.ShouldBe("success");
        factory.CreateCopyCount.ShouldBe(1);
        factory.ReleaseCopyCount.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_FactoryThrowsImmediately_ExecutionTerminated()
    {
        var factory = new ThrowingRequestCopyFactory();
        var options = new RequestCopyResilienceStrategyOptions<TestRequest>
        {
            Factory = factory,
            Ownership = RequestCopyOwnership.AdapterOwned,
        };

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
            })
            .AddRequestCopy(options)
            .Build();

        var executor = new RequestCopyExecutor<TestRequest>(pipeline, factory);
        var originalRequest = new TestRequest { Id = "original" };
        var attemptCount = 0;

        var exception = await Should.ThrowAsync<OperationCanceledException>(
            () => executor.ExecuteAsync<string>(
                originalRequest,
                (_, _, _) =>
                {
                    attemptCount++;
                    return new ValueTask<string>("success");
                },
                TestCancellation.Token).AsTask());

        attemptCount.ShouldBe(0);
        factory.CreateCopyCount.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_ResultNotDisposedByCleanup()
    {
        var factory = new TestRequestCopyFactory();
        var options = new RequestCopyResilienceStrategyOptions<TestRequest>
        {
            Factory = factory,
            Ownership = RequestCopyOwnership.AdapterOwned,
        };

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = _ => PredicateResult.False(),
            })
            .AddRequestCopy(options)
            .Build();

        var executor = new RequestCopyExecutor<TestRequest>(pipeline, factory);
        var originalRequest = new TestRequest { Id = "original" };

        var result = await executor.ExecuteAsync<DisposableResult>(
            originalRequest,
            (_, _, _) => new ValueTask<DisposableResult>(new DisposableResult()),
            TestCancellation.Token);

        result.ShouldNotBeNull();
        result.IsDisposed.ShouldBeFalse();
        factory.ReleaseCopyCount.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_OriginalRequestNotDisposed()
    {
        var factory = new TestRequestCopyFactory();
        var options = new RequestCopyResilienceStrategyOptions<TestRequest>
        {
            Factory = factory,
            Ownership = RequestCopyOwnership.AdapterOwned,
        };

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = _ => PredicateResult.True(),
            })
            .AddRequestCopy(options)
            .Build();

        var executor = new RequestCopyExecutor<TestRequest>(pipeline, factory);
        var originalRequest = new TestRequest { Id = "original" };

        await executor.ExecuteAsync<string>(
            originalRequest,
            (_, request, _) =>
            {
                if (request.Id == "copy-1")
                {
                    throw new InvalidOperationException("first attempt failed");
                }
                return new ValueTask<string>("success");
            },
            TestCancellation.Token);

        originalRequest.IsDisposed.ShouldBeFalse();
    }

    private class TestRequest
    {
        public string Id { get; set; } = string.Empty;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private class TestRequestCopyFactory : RequestCopyFactory<TestRequest>
    {
        private int _createCopyCount;
        private int _releaseCopyCount;

        public int CreateCopyCount => _createCopyCount;

        public int ReleaseCopyCount => _releaseCopyCount;

        public override TestRequest CreateCopy(TestRequest request)
        {
            Interlocked.Increment(ref _createCopyCount);
            return new TestRequest { Id = $"copy-{_createCopyCount}" };
        }

        public override void ReleaseCopy(TestRequest copy)
        {
            Interlocked.Increment(ref _releaseCopyCount);
            copy.Dispose();
        }
    }

    private class ThrowingRequestCopyFactory : RequestCopyFactory<TestRequest>
    {
        private int _createCopyCount;

        public int CreateCopyCount => _createCopyCount;

        public override TestRequest CreateCopy(TestRequest request)
        {
            Interlocked.Increment(ref _createCopyCount);
            throw new InvalidOperationException("Factory failed to create copy");
        }

        public override void ReleaseCopy(TestRequest copy)
        {
        }
    }

    private class DisposableResult : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
