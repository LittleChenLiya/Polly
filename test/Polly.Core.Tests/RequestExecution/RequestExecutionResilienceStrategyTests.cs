// The requests created in this fixture are disposed by the adapter under test,
// which the analyzer cannot track.
#pragma warning disable CA2000 // Dispose objects before losing scope
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Time.Testing;
using Polly.RequestExecution;
using Polly.Retry;

namespace Polly.Core.Tests.RequestExecution;

public class RequestExecutionResilienceStrategyTests
{
    [Fact]
    public async Task Retry_FirstFailsSecondSucceeds_CopiesDifferAndEachReleasedOnce()
    {
        var original = new DisposableRequest();
        var factoryCalls = 0;
        var createdCopies = new List<DisposableRequest>();
        var attempts = 0;

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(new RetryStrategyOptions<string>
            {
                ShouldHandle = new PredicateBuilder<string>().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                CopyFactory = (request, _) =>
                {
                    request.ShouldBeSameAs(original);
                    factoryCalls++;
                    var copy = request.Clone(factoryCalls);
                    createdCopies.Add(copy);
                    return new ValueTask<DisposableRequest>(copy);
                },
                ExecuteCallback = (request, _) =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        request.ShouldBeSameAs(createdCopies[0]);
                        return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("boom")));
                    }

                    request.ShouldBeSameAs(createdCopies[1]);
                    return new ValueTask<Outcome<string>>(Outcome.FromResult("success"));
                },
            })
            .Build();

        var result = await pipeline.ExecuteAsync(
            _ => new ValueTask<string>("unused"),
            CancellationToken.None);

        result.ShouldBe("success");
        factoryCalls.ShouldBe(2);
        attempts.ShouldBe(2);
        createdCopies.Count.ShouldBe(2);
        createdCopies[0].ShouldNotBeSameAs(createdCopies[1]);
        createdCopies.ShouldAllBe(copy => !ReferenceEquals(copy, original));
        createdCopies[0].IsDisposed.ShouldBeTrue();
        createdCopies[1].IsDisposed.ShouldBeTrue();
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task FactoryThrowsOnSecondAttempt_CallbackNotInvokedAndTerminatesImmediately()
    {
        var original = new DisposableRequest();
        var factoryCalls = 0;
        var callbackCalls = 0;
        var firstCopy = new DisposableRequest();

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(new RetryStrategyOptions<string>
            {
                // Only failures reported by the execution callback are retryable;
                // a failing copy factory is not an execution outcome and terminates immediately.
                ShouldHandle = new PredicateBuilder<string>().Handle<TransientExecutionException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                CopyFactory = (_, _) =>
                {
                    factoryCalls++;
                    if (factoryCalls == 1)
                    {
                        return new ValueTask<DisposableRequest>(firstCopy);
                    }

                    throw new RequestCopyFactoryException("cannot create another copy");
                },
                ExecuteCallback = (_, _) =>
                {
                    callbackCalls++;
                    return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new TransientExecutionException("retry me")));
                },
            })
            .Build();

        var exception = await Should.ThrowAsync<RequestCopyFactoryException>(async () =>
            await pipeline.ExecuteAsync(_ => new ValueTask<string>("unused"), CancellationToken.None));

        exception.Message.ShouldBe("cannot create another copy");
        factoryCalls.ShouldBe(2);
        callbackCalls.ShouldBe(1);
        firstCopy.IsDisposed.ShouldBeTrue();
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task CanceledWhileWaitingForRetry_NoThirdCopyIsCreated()
    {
        var timeProvider = new FakeTimeProvider();
        var original = new DisposableRequest();
        var factoryCalls = 0;
        var callbackCalls = 0;
        using var cts = new CancellationTokenSource();

        var pipeline = new ResiliencePipelineBuilder<string> { TimeProvider = timeProvider }
            .AddRetry(new RetryStrategyOptions<string>
            {
                ShouldHandle = new PredicateBuilder<string>().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(10),
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                CopyFactory = (_, _) =>
                {
                    factoryCalls++;
                    return new ValueTask<DisposableRequest>(original.Clone(factoryCalls));
                },
                ExecuteCallback = (_, _) =>
                {
                    callbackCalls++;
                    return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("boom")));
                },
            })
            .Build();

        var execution = pipeline.ExecuteAsync(
            _ => new ValueTask<string>("unused"),
            cts.Token);

        // The first attempt failed and the retry is waiting on the fake clock.
        factoryCalls.ShouldBe(1);
        callbackCalls.ShouldBe(1);

        cts.Cancel();
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        await Should.ThrowAsync<OperationCanceledException>(async () => await execution);

        // No second copy was ever built because the wait was canceled first.
        factoryCalls.ShouldBe(1);
        callbackCalls.ShouldBe(1);
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task SuccessfulAttemptReturnsTheCopy_ResultIsNotReleased()
    {
        var original = new DisposableRequest();
        var failedCopy = new DisposableRequest();
        var returnedCopy = new DisposableRequest();
        var factoryCalls = 0;

        var pipeline = new ResiliencePipelineBuilder<DisposableRequest>()
            .AddRetry(new RetryStrategyOptions<DisposableRequest>
            {
                ShouldHandle = new PredicateBuilder<DisposableRequest>().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, DisposableRequest>
            {
                OriginalRequest = original,
                CopyFactory = (_, _) =>
                {
                    factoryCalls++;
                    return new ValueTask<DisposableRequest>(factoryCalls == 1 ? failedCopy : returnedCopy);
                },
                ExecuteCallback = (request, _) =>
                {
                    if (factoryCalls == 1)
                    {
                        return new ValueTask<Outcome<DisposableRequest>>(
                            Outcome.FromException<DisposableRequest>(new InvalidOperationException("first attempt fails")));
                    }

                    return new ValueTask<Outcome<DisposableRequest>>(Outcome.FromResult(request));
                },
            })
            .Build();

        var result = await pipeline.ExecuteAsync(
            _ => new ValueTask<DisposableRequest>((DisposableRequest)null!),
            CancellationToken.None);

        result.ShouldBeSameAs(returnedCopy);
        returnedCopy.IsDisposed.ShouldBeFalse();
        failedCopy.IsDisposed.ShouldBeTrue();
        factoryCalls.ShouldBe(2);
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task CallbackOwnership_AdapterNeverReleasesCopies()
    {
        var original = new DisposableRequest();
        var releasedByCallback = new List<DisposableRequest>();
        var factoryCalls = 0;
        var attempts = 0;

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(new RetryStrategyOptions<string>
            {
                ShouldHandle = new PredicateBuilder<string>().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                Ownership = RequestCopyOwnership.Callback,
                CopyFactory = (_, _) =>
                {
                    factoryCalls++;
                    return new ValueTask<DisposableRequest>(original.Clone(factoryCalls));
                },
                ReleaseCallback = request =>
                {
                    releasedByCallback.Add(request);
                    return default;
                },
                ExecuteCallback = (_, _) =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("boom")));
                    }

                    return new ValueTask<Outcome<string>>(Outcome.FromResult("success"));
                },
            })
            .Build();

        var result = await pipeline.ExecuteAsync(
            _ => new ValueTask<string>("unused"),
            CancellationToken.None);

        result.ShouldBe("success");
        attempts.ShouldBe(2);
        releasedByCallback.ShouldBeEmpty();
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExplicitReleaseCallback_InvokedOncePerFinishedAttempt()
    {
        var original = new DisposableRequest();
        var releases = new List<int>();
        var factoryCalls = 0;
        var attempts = 0;

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(new RetryStrategyOptions<string>
            {
                ShouldHandle = new PredicateBuilder<string>().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                Ownership = RequestCopyOwnership.Adapter,
                CopyFactory = (_, _) =>
                {
                    factoryCalls++;
                    return new ValueTask<DisposableRequest>(original.Clone(factoryCalls));
                },
                ReleaseCallback = request =>
                {
                    releases.Add(request.Id);
                    request.Dispose();
                    return default;
                },
                ExecuteCallback = (_, _) =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("boom")));
                    }

                    return new ValueTask<Outcome<string>>(Outcome.FromResult("success"));
                },
            })
            .Build();

        var result = await pipeline.ExecuteAsync(
            _ => new ValueTask<string>("unused"),
            CancellationToken.None);

        result.ShouldBe("success");
        releases.Count.ShouldBe(2);
        releases[0].ShouldNotBe(releases[1]);
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteCallbackThrows_ExceptionPropagatedAndCopyReleased()
    {
        var original = new DisposableRequest();
        var copy = new DisposableRequest();

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                CopyFactory = (_, _) => new ValueTask<DisposableRequest>(copy),
                ExecuteCallback = (_, _) => throw new InvalidOperationException("callback failure"),
            })
            .Build();

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync(_ => new ValueTask<string>("unused"), CancellationToken.None));

        exception.Message.ShouldBe("callback failure");
        copy.IsDisposed.ShouldBeTrue();
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task AsyncCopyFactoryFailure_TerminatesAndReleasesPartiallyCreatedCopy()
    {
        var original = new DisposableRequest();
        var producedCopy = new DisposableRequest();
        var factoryCalls = 0;
        var callbackCalls = 0;

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(new RetryStrategyOptions<string>
            {
                ShouldHandle = new PredicateBuilder<string>().Handle<TransientExecutionException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                CopyFactory = (_, _) =>
                {
                    factoryCalls++;
                    if (factoryCalls == 1)
                    {
                        return new ValueTask<DisposableRequest>(producedCopy);
                    }

                    return AsyncFactoryFailure();
                },
                ExecuteCallback = (_, _) =>
                {
                    callbackCalls++;
                    return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new TransientExecutionException("retry me")));
                },
            })
            .Build();

        var exception = await Should.ThrowAsync<RequestCopyFactoryException>(async () =>
            await pipeline.ExecuteAsync(_ => new ValueTask<string>("unused"), CancellationToken.None));

        exception.Message.ShouldBe("async factory boom");
        factoryCalls.ShouldBe(2);
        callbackCalls.ShouldBe(1);
        producedCopy.IsDisposed.ShouldBeTrue();
        original.IsDisposed.ShouldBeFalse();

        static async ValueTask<DisposableRequest> AsyncFactoryFailure()
        {
            await Task.Yield();
            throw new RequestCopyFactoryException("async factory boom");
        }
    }

    [Fact]
    public void SyncExecution_RetryUsesFreshCopies()
    {
        var original = new DisposableRequest();
        var factoryCalls = 0;
        var attempts = 0;

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(new RetryStrategyOptions<string>
            {
                ShouldHandle = new PredicateBuilder<string>().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                CopyFactory = (_, _) =>
                {
                    factoryCalls++;
                    return new ValueTask<DisposableRequest>(original.Clone(factoryCalls));
                },
                ExecuteCallback = (_, _) =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("boom")));
                    }

                    return new ValueTask<Outcome<string>>(Outcome.FromResult("sync-success"));
                },
            })
            .Build();

        var result = pipeline.Execute(_ => "unused", CancellationToken.None);

        result.ShouldBe("sync-success");
        factoryCalls.ShouldBe(2);
        attempts.ShouldBe(2);
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public void AddRequestExecution_NullArguments_Throws()
    {
        var builder = new ResiliencePipelineBuilder<string>();
        var options = new RequestExecutionStrategyOptions<DisposableRequest, string>();

        Should.Throw<ArgumentNullException>(() =>
            builder.AddRequestExecution<DisposableRequest, string>(null!));

        options.OriginalRequest = new DisposableRequest();
        Should.Throw<ValidationException>(() => builder.AddRequestExecution(options));

        options.CopyFactory = (request, _) => new ValueTask<DisposableRequest>(request.Clone(1));
        Should.Throw<ValidationException>(() => builder.AddRequestExecution(options));
    }

    [Fact]
    public async Task NonGenericPipeline_VoidResult_UsesFreshCopies()
    {
        var original = new DisposableRequest();
        var factoryCalls = 0;
        var attempts = 0;

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new VoidRequestExecutionStrategyOptions<DisposableRequest>
            {
                OriginalRequest = original,
                CopyFactory = (request, _) =>
                {
                    factoryCalls++;
                    return new ValueTask<DisposableRequest>(request.Clone(factoryCalls));
                },
                ExecuteCallback = (_, _) =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        throw new InvalidOperationException("boom");
                    }

                    return default;
                },
            })
            .Build();

        await pipeline.ExecuteAsync(_ => default, CancellationToken.None);

        factoryCalls.ShouldBe(2);
        attempts.ShouldBe(2);
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task AlreadyCanceled_FactoryNeverInvoked()
    {
        var original = new DisposableRequest();
        var factoryCalls = 0;
        var callbackCalls = 0;

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRetry(new RetryStrategyOptions<string>
            {
                ShouldHandle = new PredicateBuilder<string>().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
            })
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                CopyFactory = (request, _) =>
                {
                    factoryCalls++;
                    return new ValueTask<DisposableRequest>(request.Clone(factoryCalls));
                },
                ExecuteCallback = (_, _) =>
                {
                    callbackCalls++;
                    return new ValueTask<Outcome<string>>(Outcome.FromResult("success"));
                },
            })
            .Build();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await pipeline.ExecuteAsync(_ => new ValueTask<string>("unused"), cts.Token));

        factoryCalls.ShouldBe(0);
        callbackCalls.ShouldBe(0);
        original.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public async Task FactoryReturnsNull_TerminatesImmediately()
    {
        var original = new DisposableRequest();
        var callbackCalls = 0;

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddRequestExecution(new RequestExecutionStrategyOptions<DisposableRequest, string>
            {
                OriginalRequest = original,
                CopyFactory = (_, _) => new ValueTask<DisposableRequest>((DisposableRequest)null!),
                ExecuteCallback = (_, _) =>
                {
                    callbackCalls++;
                    return new ValueTask<Outcome<string>>(Outcome.FromResult("success"));
                },
            })
            .Build();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync(_ => new ValueTask<string>("unused"), CancellationToken.None));

        callbackCalls.ShouldBe(0);
        original.IsDisposed.ShouldBeFalse();
    }

    private sealed class TransientExecutionException : Exception
    {
        public TransientExecutionException(string message)
            : base(message)
        {
        }
    }

    private sealed class RequestCopyFactoryException : Exception
    {
        public RequestCopyFactoryException(string message)
            : base(message)
        {
        }
    }
}
