# Testing

This document explains how to test Polly's resilience pipelines. You should not test how the resilience pipelines operate internally, but rather test your own settings or custom delegates.

To make the testing process simpler, Polly offers the [`Polly.Testing`](https://www.nuget.org/packages/Polly.Testing/) package. This package has a range of APIs designed to help you test the setup and combination of resilience pipelines in your user code.

## Usage

Begin by adding the [`Polly.Testing`](https://www.nuget.org/packages/Polly.Testing) package to your test project:

```sh
dotnet add package Polly.Testing
```

Use the `GetPipelineDescriptor` extension method to get the [`ResiliencePipelineDescriptor`](xref:Polly.Testing.ResiliencePipelineDescriptor) which provides details on the pipeline's composition:

<!-- snippet: get-pipeline-descriptor -->
```cs
// Build your resilience pipeline.
ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 4
    })
    .AddTimeout(TimeSpan.FromSeconds(1))
    .Build();

// Retrieve the descriptor.
ResiliencePipelineDescriptor descriptor = pipeline.GetPipelineDescriptor();

// Check the pipeline's composition with the descriptor.
Assert.Equal(2, descriptor.Strategies.Count);

// Verify the retry settings.
var retryOptions = Assert.IsType<RetryStrategyOptions>(descriptor.Strategies[0].Options);
Assert.Equal(4, retryOptions.MaxRetryAttempts);

// Confirm the timeout settings.
var timeoutOptions = Assert.IsType<TimeoutStrategyOptions>(descriptor.Strategies[1].Options);
Assert.Equal(TimeSpan.FromSeconds(1), timeoutOptions.Timeout);
```
<!-- endSnippet -->

The `GetPipelineDescriptor` extension method is also available for the generic `ResiliencePipeline<T>`:

<!-- snippet: get-pipeline-descriptor-generic -->
```cs
// Construct your resilience pipeline.
ResiliencePipeline<string> pipeline = new ResiliencePipelineBuilder<string>()
    .AddRetry(new RetryStrategyOptions<string>
    {
        MaxRetryAttempts = 4
    })
    .AddTimeout(TimeSpan.FromSeconds(1))
    .Build();

// Obtain the descriptor.
ResiliencePipelineDescriptor descriptor = pipeline.GetPipelineDescriptor();

// Check the pipeline's composition with the descriptor.
// ...
```
<!-- endSnippet -->

## Collecting telemetry events

Use the [`TelemetryEventCollector`](xref:Polly.Testing.TelemetryEventCollector) to capture the telemetry events of a single pipeline invocation and take them out of the pipeline for analysis. The collector is a `TelemetryListener` that composes with your existing telemetry instead of replacing it: pass the listener you already use to the constructor and it keeps receiving every event.

Each invocation collects its own isolated set of events, even when invocations run concurrently. When the invocation is disposed, the collected events are sealed into an immutable snapshot that preserves the event names, their order, the attempt numbers and the outcome categories. Only explicitly supported scalar values are copied into the snapshot; any other object is represented by its type name, so the collector never calls `ToString` on your objects and never stores mutable originals. If the number of events exceeds the configured limit, the extra events are only counted as dropped and are not stored. The collector never throws, so it cannot change the outcome of the pipeline under test.

<!-- snippet: telemetry-event-collector -->
```cs
// Create the collector. If you already use a telemetry listener, pass it
// to the constructor so it keeps receiving every event.
var collector = new TelemetryEventCollector();

ResiliencePipeline<string> pipeline = new ResiliencePipelineBuilder<string> { TelemetryListener = collector }
    .AddRetry(new RetryStrategyOptions<string>
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.Zero,
    })
    .Build();

// Collect the telemetry of a single invocation.
using (collector.BeginInvocation())
{
    pipeline.Execute(static _ => "result");
}

// Disposing the invocation seals the collected events into an immutable snapshot.
TelemetryInvocationSnapshot snapshot = collector.GetSnapshots().Single();

// Print a report of the collected events.
Console.WriteLine("Collected {0} events ({1} dropped):", snapshot.Events.Count, snapshot.DroppedEvents);
foreach (CollectedTelemetryEvent e in snapshot.Events)
{
    Console.WriteLine(
        "#{0} {1}: attempt={2}, outcome={3}",
        e.Sequence,
        e.EventName,
        e.Attempt is int attempt ? attempt : "-",
        e.Outcome);
}
```
<!-- endSnippet -->

The example above prints a report similar to:

```text
Collected 3 events (0 dropped):
#0 PipelineExecuting: attempt=-, outcome=None
#1 ExecutionAttempt: attempt=0, outcome=Result
#2 PipelineExecuted: attempt=-, outcome=Result
```

## Mocking `ResiliencePipelineProvider<TKey>`

Consider the following code that might resemble a part of your project:

<!-- snippet: testing-resilience-pipeline-provider-usage -->
```cs
// Represents an arbitrary API that needs resilience support
public class MyApi
{
    private readonly ResiliencePipeline _pipeline;

    // The value of pipelineProvider is injected via dependency injection
    public MyApi(ResiliencePipelineProvider<string> pipelineProvider)
    {
        _pipeline = pipelineProvider.GetPipeline("my-pipeline");
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _pipeline.ExecuteAsync(
            static async token =>
            {
                // Add your code here
            },
            cancellationToken);
    }
}

// Extensions to incorporate MyApi into dependency injection
public static class MyApiExtensions
{
    public static IServiceCollection AddMyApi(this IServiceCollection services)
    {
        return services
            .AddResiliencePipeline("my-pipeline", builder =>
            {
                builder.AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 4
                });
            })
            .AddSingleton<MyApi>();
    }
}
```
<!-- endSnippet -->

In the example above:

- The `MyApi` class is introduced, representing part of your application that requires resilience support.
- The `AddMyApi` extension method is also defined, which integrates `MyApi` into dependency injection (DI) and sets up the resilience pipeline it uses.

For unit tests, if you want to assess the behavior of `ExecuteAsync`, it might not be practical to rely on the entire pipeline, especially since it could slow down tests during failure scenario evaluations. Instead, it's recommended to mock the `ResiliencePipelineProvider<string>` and return an empty pipeline:

<!-- snippet: testing-resilience-pipeline-provider-mocking -->
```cs
ResiliencePipelineProvider<string> pipelineProvider = Substitute.For<ResiliencePipelineProvider<string>>();

// Mock the pipeline provider to return an empty pipeline for testing
pipelineProvider
    .GetPipeline("my-pipeline")
    .Returns(ResiliencePipeline.Empty);

// Use the mocked pipeline provider in your code
var api = new MyApi(pipelineProvider);

// You can now test the api
```
<!-- endSnippet -->

This example leverages the [`NSubstitute`](https://github.com/nsubstitute/NSubstitute) library to mock the pipeline provider.
