using Polly.Fallback;

namespace Polly.Core.Tests.Fallback;

public class OrderedFallbackTests
{
    [Fact]
    public async Task ExecuteAsync_FallbackableFailureThenSuccess_ReturnsSecondResultAndSkipsRemaining()
    {
        // Arrange: three candidates - fallbackable failure, success, and one that must never run
        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var invocations = new List<string>();
        var thirdCandidateCalls = 0;
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
        {
            _ => { invocations.Add("first"); throw new InvalidOperationException("transient"); },
            _ => { invocations.Add("second"); return new ValueTask<string>("second-result"); },
            _ => { thirdCandidateCalls++; return new ValueTask<string>("third-result"); },
        };

        // Act
        var result = await OrderedFallback.ExecuteAsync(context, candidates, _ => true);

        // Assert
        result.ShouldBe("second-result");
        invocations.ShouldBe(["first", "second"]);
        thirdCandidateCalls.ShouldBe(0);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_NonFallbackableFailure_StopsImmediately()
    {
        // Arrange
        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var secondCandidateCalls = 0;
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
        {
            _ => throw new InvalidOperationException("fatal"),
            _ => { secondCandidateCalls++; return new ValueTask<string>("ok"); },
        };

        // Act
        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => OrderedFallback.ExecuteAsync(context, candidates, e => e is not InvalidOperationException).AsTask());

        // Assert: the original error is rethrown as-is and no further candidate runs
        error.Message.ShouldBe("fatal");
        secondCandidateCalls.ShouldBe(0);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_AllCandidatesFail_PreservesErrorsInInvocationOrder()
    {
        // Arrange
        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
        {
            _ => throw new InvalidOperationException("first"),
            _ => throw new FormatException("second"),
            _ => throw new NotSupportedException("third"),
        };

        // Act
        var error = await Should.ThrowAsync<AggregateException>(
            () => OrderedFallback.ExecuteAsync(context, candidates, _ => true).AsTask());

        // Assert: errors are preserved in the exact order the candidates were invoked
        error.InnerExceptions.Select(e => e.Message).ShouldBe(["first", "second", "third"]);
        error.InnerExceptions.Select(e => e.GetType()).ShouldBe(
            [typeof(InvalidOperationException), typeof(FormatException), typeof(NotSupportedException)]);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelledToken_StopsBeforeFirstCandidate()
    {
        // Arrange
        var context = ResilienceContextPool.Shared.Get(new CancellationToken(canceled: true));
        var calls = 0;
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
        {
            _ => { calls++; return new ValueTask<string>("ok"); },
        };

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => OrderedFallback.ExecuteAsync(context, candidates, _ => true).AsTask());
        calls.ShouldBe(0);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationBetweenCandidates_StopsImmediately()
    {
        // Arrange: the first candidate cancels the token and then fails with a fallbackable error
        using var cts = new CancellationTokenSource();
        var context = ResilienceContextPool.Shared.Get(cts.Token);
        var secondCandidateCalls = 0;
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
        {
            _ => { cts.Cancel(); throw new InvalidOperationException("transient"); },
            _ => { secondCandidateCalls++; return new ValueTask<string>("ok"); },
        };

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => OrderedFallback.ExecuteAsync(context, candidates, _ => true).AsTask());
        secondCandidateCalls.ShouldBe(0);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_CandidateThrowsCancellation_StopsImmediatelyEvenIfFallbackable()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var context = ResilienceContextPool.Shared.Get(cts.Token);
        var secondCandidateCalls = 0;
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
        {
            _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); },
            _ => { secondCandidateCalls++; return new ValueTask<string>("ok"); },
        };

        // Act & Assert: cancellation is never treated as a fallbackable error
        await Should.ThrowAsync<OperationCanceledException>(
            () => OrderedFallback.ExecuteAsync(context, candidates, _ => true).AsTask());
        secondCandidateCalls.ShouldBe(0);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationNotLinkedToContextToken_PredicateDecides()
    {
        // Arrange: an OperationCanceledException unrelated to the context token is a regular error
        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
        {
            _ => throw new OperationCanceledException("unrelated"),
            _ => new ValueTask<string>("recovered"),
        };

        // Act
        var result = await OrderedFallback.ExecuteAsync(context, candidates, _ => true);

        // Assert
        result.ShouldBe("recovered");
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCandidates_Throws()
    {
        // Arrange
        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>();

        // Act & Assert
        var error = await Should.ThrowAsync<ArgumentException>(
            () => OrderedFallback.ExecuteAsync(context, candidates, _ => true).AsTask());
        error.ParamName.ShouldBe("candidates");
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_CandidatesMutatedDuringExecution_SnapshotPreventsExtraRuns()
    {
        // Arrange: the first candidate appends an extra candidate to the original list
        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var invocations = new List<string>();
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>>
        {
            _ => { invocations.Add("first"); throw new InvalidOperationException("transient"); },
            _ => { invocations.Add("second"); return new ValueTask<string>("second-result"); },
        };
        candidates.Insert(0, _ =>
        {
            invocations.Add("mutating");
            candidates.Add(_ => { invocations.Add("late-addition"); return new ValueTask<string>("late"); });
            throw new InvalidOperationException("transient");
        });

        // Act
        var result = await OrderedFallback.ExecuteAsync(context, candidates, _ => true);

        // Assert: the late addition is never executed even though all earlier failures were fallbackable
        result.ShouldBe("second-result");
        invocations.ShouldBe(["mutating", "first", "second"]);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task ExecuteAsync_NullArguments_Throws()
    {
        var context = ResilienceContextPool.Shared.Get(TestCancellation.Token);
        var candidates = new List<Func<ResilienceContext, ValueTask<string>>> { _ => new ValueTask<string>("ok") };

        await Should.ThrowAsync<ArgumentNullException>(async () => await OrderedFallback.ExecuteAsync<string>(null!, candidates, _ => true));
        await Should.ThrowAsync<ArgumentNullException>(async () => await OrderedFallback.ExecuteAsync<string>(context, null!, _ => true));
        await Should.ThrowAsync<ArgumentNullException>(async () => await OrderedFallback.ExecuteAsync(context, candidates, null!));

        ResilienceContextPool.Shared.Return(context);
    }
}
