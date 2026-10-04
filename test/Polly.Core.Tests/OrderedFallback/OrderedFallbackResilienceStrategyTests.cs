using Polly.OrderedFallback;
using Polly.Telemetry;

namespace Polly.Core.Tests.OrderedFallback;

public class OrderedFallbackResilienceStrategyTests
{
    private readonly OrderedFallbackStrategyOptions<string> _options = new();
    private readonly List<TelemetryEventArguments<object, object>> _args = [];
    private readonly ResilienceStrategyTelemetry _telemetry;
    private OrderedFallbackHandler<string>? _handler;

    public OrderedFallbackResilienceStrategyTests() => _telemetry = TestUtilities.CreateResilienceTelemetry(_args.Add);

    [Fact]
    public void Ctor_Ok() =>
        Create().ShouldNotBeNull();

    [Fact]
    public void Handle_PrimarySuccess_ReturnsPrimary()
    {
        var candidateCalled = false;
        SetHandler(
            _ => false,
            () => { candidateCalled = true; return Outcome.FromResult("fallback"); });

        Create().Execute(_ => "primary", TestCancellation.Token).ShouldBe("primary");
        candidateCalled.ShouldBeFalse();
    }

    [Fact]
    public void Handle_ThreeCandidates_FallbackableFailureSuccessCountNotExecuted()
    {
        var candidate3Called = false;
        var callOrder = new List<string>();

        var candidates = new List<OrderedFallbackCandidate<string>>
        {
            new()
            {
                Name = "candidate-1",
                Action = _ =>
                {
                    callOrder.Add("candidate-1");
                    return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("candidate-1 failed")));
                }
            },
            new()
            {
                Name = "candidate-2",
                Action = _ =>
                {
                    callOrder.Add("candidate-2");
                    return new ValueTask<Outcome<string>>(Outcome.FromResult("second-result"));
                }
            },
            new()
            {
                Name = "candidate-3",
                Action = _ =>
                {
                    callOrder.Add("candidate-3");
                    candidate3Called = true;
                    return new ValueTask<Outcome<string>>(Outcome.FromResult("third-result"));
                }
            }
        };

        _handler = OrderedFallbackHelper.CreateHandler(
            outcome => outcome.Exception is InvalidOperationException,
            candidates);

        var result = Create().Execute<string>(_ => throw new InvalidOperationException("primary failed"), TestCancellation.Token);

        result.ShouldBe("second-result");
        candidate3Called.ShouldBeFalse();
        callOrder.ShouldBe(new[] { "candidate-1", "candidate-2" });
    }

    [Fact]
    public void Handle_FirstNonFallbackableFailure_StopsImmediately()
    {
        var candidateCalled = false;

        var candidates = new List<OrderedFallbackCandidate<string>>
        {
            new()
            {
                Name = "candidate-1",
                Action = _ =>
                {
                    candidateCalled = true;
                    return new ValueTask<Outcome<string>>(Outcome.FromResult("fallback"));
                }
            }
        };

        _handler = OrderedFallbackHelper.CreateHandler(
            outcome => outcome.Exception is InvalidOperationException,
            candidates);

        Should.Throw<ArgumentException>(() => Create().Execute<string>(_ => throw new ArgumentException("non-fallbackable"), TestCancellation.Token));
        candidateCalled.ShouldBeFalse();
    }

    [Fact]
    public void Handle_AllFail_ErrorsPreservedInCallOrder()
    {
        var callOrder = new List<string>();

        var candidates = new List<OrderedFallbackCandidate<string>>
        {
            new()
            {
                Name = "candidate-1",
                Action = _ =>
                {
                    callOrder.Add("candidate-1");
                    return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("candidate-1 failed")));
                }
            },
            new()
            {
                Name = "candidate-2",
                Action = _ =>
                {
                    callOrder.Add("candidate-2");
                    return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("candidate-2 failed")));
                }
            },
            new()
            {
                Name = "candidate-3",
                Action = _ =>
                {
                    callOrder.Add("candidate-3");
                    return new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("candidate-3 failed")));
                }
            }
        };

        _handler = OrderedFallbackHelper.CreateHandler(
            outcome => outcome.Exception is InvalidOperationException,
            candidates);

        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var outcome = Create().ExecuteOutcomeAsync<string, string>((_, _) => throw new InvalidOperationException("primary failed"), context, "dummy-state").GetAwaiter().GetResult();
        ResilienceContextPool.Shared.Return(context);

        callOrder.ShouldBe(new[] { "candidate-1", "candidate-2", "candidate-3" });

        outcome.Exception.ShouldBeOfType<AggregateException>();
        var aggregate = (AggregateException)outcome.Exception;
        aggregate.InnerExceptions.Count.ShouldBe(3);
        aggregate.InnerExceptions[0].Message.ShouldBe("candidate-1 failed");
        aggregate.InnerExceptions[1].Message.ShouldBe("candidate-2 failed");
        aggregate.InnerExceptions[2].Message.ShouldBe("candidate-3 failed");
    }

    [Fact]
    public void Handle_Cancellation_StopsImmediately()
    {
        var candidateCalled = false;
        using var cts = new CancellationTokenSource();

        var candidates = new List<OrderedFallbackCandidate<string>>
        {
            new()
            {
                Name = "candidate-1",
                Action = _ =>
                {
                    candidateCalled = true;
                    return new ValueTask<Outcome<string>>(Outcome.FromResult("fallback"));
                }
            }
        };

        _handler = OrderedFallbackHelper.CreateHandler(
            outcome => outcome.Exception is InvalidOperationException,
            candidates);

        // Use the strategy directly to bypass the CompositeComponent cancellation check,
        // so we can verify that the strategy itself stops immediately when cancellation is requested.
        var strategy = new OrderedFallbackResilienceStrategy<string>(_handler!, _telemetry);
        var context = ResilienceContextPool.Shared.Get(cts.Token);
        cts.Cancel();

        var outcome = strategy.ExecuteCore(
            (_, _) => new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("primary failed"))),
            context,
            "dummy-state").GetAwaiter().GetResult();

        outcome.Exception.ShouldBeOfType<AggregateException>();
        var aggregate = (AggregateException)outcome.Exception;
        aggregate.InnerExceptions.Count.ShouldBe(1);
        aggregate.InnerExceptions[0].ShouldBeOfType<OperationCanceledException>();
        candidateCalled.ShouldBeFalse();
    }

    [Fact]
    public void Handle_EmptyCandidates_ReturnsPrimaryOutcome()
    {
        _handler = OrderedFallbackHelper.CreateHandler(
            outcome => outcome.Exception is InvalidOperationException,
            Array.Empty<Func<Outcome<string>>>());

        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var outcome = Create().ExecuteOutcomeAsync<string, string>((_, _) => throw new InvalidOperationException("primary failed"), context, "dummy-state").GetAwaiter().GetResult();
        ResilienceContextPool.Shared.Return(context);

        outcome.Exception.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void Handle_ConfigModifiedDuringExecution_DoesNotRunExtraCandidate()
    {
        var extraCandidateCalled = false;
        var candidates = new List<OrderedFallbackCandidate<string>>();
        candidates.Add(new OrderedFallbackCandidate<string>
        {
            Name = "candidate-1",
            Action = _ =>
            {
                candidates.Add(new OrderedFallbackCandidate<string>
                {
                    Name = "extra-candidate",
                    Action = _ =>
                    {
                        extraCandidateCalled = true;
                        return new ValueTask<Outcome<string>>(Outcome.FromResult("extra"));
                    }
                });
                return new ValueTask<Outcome<string>>(Outcome.FromResult("first-result"));
            }
        });

        _handler = OrderedFallbackHelper.CreateHandler(
            outcome => outcome.Exception is InvalidOperationException,
            candidates);

        var result = Create().Execute<string>(_ => throw new InvalidOperationException("primary failed"), TestCancellation.Token);

        result.ShouldBe("first-result");
        extraCandidateCalled.ShouldBeFalse();
    }

    [Fact]
    public void Handle_OnOrderedFallback_RaisedForEachCandidate()
    {
        var onFallbackCalls = new List<string?>();

        var candidates = new List<OrderedFallbackCandidate<string>>
        {
            new()
            {
                Name = "candidate-1",
                Action = _ => new ValueTask<Outcome<string>>(Outcome.FromException<string>(new InvalidOperationException("candidate-1 failed")))
            },
            new()
            {
                Name = "candidate-2",
                Action = _ => new ValueTask<Outcome<string>>(Outcome.FromResult("second-result"))
            }
        };

        _handler = OrderedFallbackHelper.CreateHandler(
            outcome => outcome.Exception is InvalidOperationException,
            candidates,
            args =>
            {
                onFallbackCalls.Add(args.CandidateName);
                return default;
            });

        Create().Execute<string>(_ => throw new InvalidOperationException("primary failed"), TestCancellation.Token);

        onFallbackCalls.ShouldBe(new[] { "candidate-1", "candidate-2" });
    }

    [Fact]
    public void Handle_CandidateThrows_PreservesException()
    {
        var callOrder = new List<string>();

        var candidates = new List<OrderedFallbackCandidate<string>>
        {
            new()
            {
                Name = "candidate-1",
                Action = _ =>
                {
                    callOrder.Add("candidate-1");
                    throw new InvalidOperationException("candidate-1 threw");
                }
            },
            new()
            {
                Name = "candidate-2",
                Action = _ =>
                {
                    callOrder.Add("candidate-2");
                    return new ValueTask<Outcome<string>>(Outcome.FromResult("second-result"));
                }
            }
        };

        _handler = OrderedFallbackHelper.CreateHandler(
            outcome => outcome.Exception is InvalidOperationException,
            candidates);

        var result = Create().Execute<string>(_ => throw new InvalidOperationException("primary failed"), TestCancellation.Token);

        result.ShouldBe("second-result");
        callOrder.ShouldBe(new[] { "candidate-1", "candidate-2" });
    }

    private void SetHandler(
        Func<Outcome<string>, bool> shouldHandle,
        params Func<Outcome<string>>[] fallback) =>
        _handler = OrderedFallbackHelper.CreateHandler(shouldHandle, fallback);

    private ResiliencePipeline<string> Create()
        => new OrderedFallbackResilienceStrategy<string>(_handler!, _telemetry).AsPipeline();
}
