namespace Polly.OrderedFallback;

internal sealed record class OrderedFallbackHandler<T>(
    Func<OrderedFallbackPredicateArguments<T>, ValueTask<bool>> ShouldHandle,
    IReadOnlyList<OrderedFallbackCandidate<T>> Candidates,
    Func<OnOrderedFallbackArguments<T>, ValueTask>? OnOrderedFallback);
