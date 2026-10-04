# About Polly.Testing

This package exposes APIs and utilities that can be used to assert on the composition of resilience pipelines.

See [the documentation](https://www.pollydocs.org/advanced/testing) for more details.

## Usage

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

## Collecting telemetry events

Use the `TelemetryEventCollector` to capture the telemetry events of a single pipeline invocation and take them out of the pipeline for analysis. The collector composes with your existing telemetry listener instead of replacing it, seals each invocation into an immutable snapshot, and never changes the outcome of the pipeline under test.

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
