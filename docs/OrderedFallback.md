# Ordered Fallback

The ordered fallback strategy allows you to try a small number of fallback candidates in sequence after a service call fails. Each candidate is called at most once, and the first successful result ends the fallback chain.

## Key Features

- **Sequential execution**: Candidates are tried in order, not in parallel. This ensures the order of failures is deterministic.
- **First success ends**: If a candidate succeeds, its result is returned immediately.
- **Fallbackable errors only**: Only errors that the caller determines are fallbackable (via the `ShouldHandle` predicate) enter the next candidate.
- **Cancellation stops immediately**: When cancellation is requested, the strategy stops immediately and returns the cancellation exception.
- **All failures preserved in order**: If all candidates fail, the errors are preserved in the actual call order (as an `AggregateException`).
- **Fixed candidate snapshot**: The candidate list is captured as a fixed copy before execution. External modifications to the list during execution do not cause extra candidates to be run.

## Usage

### Basic Usage

```csharp
var options = new OrderedFallbackStrategyOptions<string>
{
    ShouldHandle = args => args.Outcome.Exception is InvalidOperationException,
    Candidates =
    {
        new OrderedFallbackCandidate<string>
        {
            Name = "secondary",
            Action = args => new ValueTask<Outcome<string>>(Outcome.FromResult("secondary-result"))
        },
        new OrderedFallbackCandidate<string>
        {
            Name = "tertiary",
            Action = args => new ValueTask<Outcome<string>>(Outcome.FromResult("tertiary-result"))
        }
    }
};

var pipeline = new ResiliencePipelineBuilder<string>()
    .AddOrderedFallback(options)
    .Build();

var result = pipeline.Execute(_ => throw new InvalidOperationException("primary failed"), cancellationToken);
// result: "secondary-result"
```

### Configuration

The `OrderedFallbackStrategyOptions<TResult>` class has the following properties:

| Property | Type | Description |
|----------|------|-------------|
| `ShouldHandle` | `Func<OrderedFallbackPredicateArguments<TResult>, ValueTask<bool>>` | A predicate that determines whether the fallback should be executed for a given outcome. |
| `Candidates` | `IList<OrderedFallbackCandidate<TResult>>` | The ordered list of fallback candidates to try. |
| `OnOrderedFallback` | `Func<OnOrderedFallbackArguments<TResult>, ValueTask>?` | An event delegate that is raised when a fallback candidate is about to be executed. |

The `OrderedFallbackCandidate<TResult>` class has the following properties:

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string?` | The name of the fallback candidate. |
| `Action` | `Func<OrderedFallbackActionArguments<TResult>, ValueTask<Outcome<TResult>>>?` | The fallback action to be executed when this candidate is selected. |

## Cancellation Semantics

The ordered fallback strategy reuses the `ResilienceContext` cancellation semantics. Before each candidate is executed, the strategy checks if cancellation is requested. If cancellation is requested, the strategy stops immediately and returns the cancellation exception.

```csharp
using var cts = new CancellationTokenSource();
cts.Cancel();

var result = pipeline.Execute(_ => throw new InvalidOperationException("primary failed"), cts.Token);
// result: throws OperationCanceledException (wrapped in AggregateException)
```

## Empty Candidate List

If the candidate list is empty, the primary outcome is returned as-is.

```csharp
var options = new OrderedFallbackStrategyOptions<string>
{
    ShouldHandle = args => args.Outcome.Exception is InvalidOperationException,
    Candidates = { } // empty list
};

var pipeline = new ResiliencePipelineBuilder<string>()
    .AddOrderedFallback(options)
    .Build();

var result = pipeline.Execute(_ => throw new InvalidOperationException("primary failed"), cancellationToken);
// result: throws InvalidOperationException
```

## Configuration Snapshot

The candidate list is captured as a fixed copy before execution. External modifications to the list during execution do not cause extra candidates to be run.

```csharp
var candidates = new List<OrderedFallbackCandidate<string>>
{
    new()
    {
        Name = "candidate-1",
        Action = args =>
        {
            // Modify the list during execution.
            candidates.Add(new OrderedFallbackCandidate<string>
            {
                Name = "extra-candidate",
                Action = args => new ValueTask<Outcome<string>>(Outcome.FromResult("extra"))
            });
            return new ValueTask<Outcome<string>>(Outcome.FromResult("first-result"));
        }
    }
};

var options = new OrderedFallbackStrategyOptions<string>
{
    ShouldHandle = args => args.Outcome.Exception is InvalidOperationException,
    Candidates = candidates
};

var pipeline = new ResiliencePipelineBuilder<string>()
    .AddOrderedFallback(options)
    .Build();

var result = pipeline.Execute(_ => throw new InvalidOperationException("primary failed"), cancellationToken);
// result: "first-result" (the extra candidate is never executed)
```

## Relationship to Existing Fallback Strategy

The ordered fallback strategy is a separate, independent entry point from the existing `FallbackResilienceStrategy`. It does not modify or affect the existing fallback strategy. The existing fallback strategy handles a single fallback action, while the ordered fallback strategy handles multiple candidates in sequence.
