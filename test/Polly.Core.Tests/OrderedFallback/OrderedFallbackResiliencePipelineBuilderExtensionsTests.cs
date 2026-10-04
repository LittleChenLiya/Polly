using System.ComponentModel.DataAnnotations;
using Polly.OrderedFallback;
using Polly.Testing;

namespace Polly.Core.Tests.OrderedFallback;

public class OrderedFallbackResiliencePipelineBuilderExtensionsTests
{
#pragma warning disable IDE0028
    public static readonly TheoryData<Action<ResiliencePipelineBuilder<int>>> OrderedFallbackOverloadsGeneric = new()
    {
        builder =>
        {
            builder.AddOrderedFallback(new OrderedFallbackStrategyOptions<int>
            {
                ShouldHandle = _ => PredicateResult.False(),
            });
        }
    };
#pragma warning restore IDE0028

    [MemberData(nameof(OrderedFallbackOverloadsGeneric))]
    [Theory]
    public void AddOrderedFallback_Generic_Ok(Action<ResiliencePipelineBuilder<int>> configure)
    {
        var builder = new ResiliencePipelineBuilder<int>();
        configure(builder);

        builder.Build().GetPipelineDescriptor().FirstStrategy.StrategyInstance.ShouldBeOfType<OrderedFallbackResilienceStrategy<int>>();
    }

    [Fact]
    public void AddOrderedFallbackT_ValidOptions_DoesNotThrow()
    {
        // Arrange: options with an empty candidate list are valid (empty list is allowed).
        var options = new OrderedFallbackStrategyOptions<double>();

        // Act & Assert: no exception should be thrown.
        Should.NotThrow(() => new ResiliencePipelineBuilder<double>().AddOrderedFallback(options));
    }

    [Fact]
    public void AddOrderedFallback_CandidatesSnapshot_IndependentOfOptions()
    {
        // Arrange: create options with one candidate, build the pipeline, then modify the options.
        var options = new OrderedFallbackStrategyOptions<string>
        {
            ShouldHandle = _ => PredicateResult.True(),
            Candidates =
            {
                new OrderedFallbackCandidate<string>
                {
                    Name = "candidate-1",
                    Action = _ => new ValueTask<Outcome<string>>(Outcome.FromResult("first"))
                }
            }
        };

        var pipeline = new ResiliencePipelineBuilder<string>()
            .AddOrderedFallback(options)
            .Build();

        // Modify the options after building.
        options.Candidates.Add(new OrderedFallbackCandidate<string>
        {
            Name = "candidate-2",
            Action = _ => new ValueTask<Outcome<string>>(Outcome.FromResult("second"))
        });

        // Act: the pipeline should still only have the original candidate (snapshot).
        var result = pipeline.Execute<string>(_ => throw new InvalidOperationException("test"), TestCancellation.Token);

        // Assert: the first candidate's result is returned (the snapshot was taken before the modification).
        result.ShouldBe("first");
    }
}
