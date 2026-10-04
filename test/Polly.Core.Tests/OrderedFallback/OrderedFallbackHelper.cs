using Polly.OrderedFallback;

namespace Polly.Core.Tests.OrderedFallback;

internal static class OrderedFallbackHelper
{
    public static OrderedFallbackHandler<T> CreateHandler<T>(
        Func<Outcome<T>, bool> shouldHandle,
        params Func<Outcome<T>>[] fallbackActions)
    {
        var candidates = fallbackActions
            .Select((action, index) => new OrderedFallbackCandidate<T>
            {
                Name = $"candidate-{index}",
                Action = _ => new ValueTask<Outcome<T>>(action())
            })
            .ToList();

        return new OrderedFallbackHandler<T>(
            args => new ValueTask<bool>(shouldHandle(args.Outcome)),
            candidates,
            null);
    }

    public static OrderedFallbackHandler<T> CreateHandler<T>(
        Func<Outcome<T>, bool> shouldHandle,
        IReadOnlyList<OrderedFallbackCandidate<T>> candidates,
        Func<OnOrderedFallbackArguments<T>, ValueTask>? onOrderedFallback = null)
        => new(
            args => new ValueTask<bool>(shouldHandle(args.Outcome)),
            candidates,
            onOrderedFallback);
}
