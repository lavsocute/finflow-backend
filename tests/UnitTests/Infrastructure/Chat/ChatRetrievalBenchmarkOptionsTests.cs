using FinFlow.Benchmarks.Benchmarking;
using FinFlow.Domain.Documents;
using FinFlow.Infrastructure;

namespace FinFlow.UnitTests.Infrastructure.Chat;

public class ChatRetrievalBenchmarkOptionsTests
{
    [Fact]
    public void DefaultOptions_UseProductionAlignedDimensionsAndScenarios()
    {
        var options = new ChatRetrievalBenchmarkOptions();

        Assert.Equal(ApplicationDbContext.DocumentChunkEmbeddingDimensions, options.EmbeddingDimensions);
        Assert.Equal(20, options.TopK);
        Assert.True(options.ReseedDatabase);
        Assert.Contains(options.Scenarios, scenario => scenario.Name == "tenant-wide");
        Assert.Contains(options.Scenarios, scenario => scenario.Name == "department-filtered");
        Assert.Contains(options.Scenarios, scenario => scenario.Name == "own-only");
    }

    [Fact]
    public void Validate_ThrowsForInvalidNumericConfiguration()
    {
        var options = new ChatRetrievalBenchmarkOptions
        {
            TenantCount = 0
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains(nameof(ChatRetrievalBenchmarkOptions.TenantCount), ex.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenScenarioUsesUnsupportedChunkType()
    {
        var options = new ChatRetrievalBenchmarkOptions
        {
            Scenarios =
            [
                new ChatRetrievalScenario(
                    "invalid",
                    UseDepartmentFilter: false,
                    UseOwnerFilter: false,
                    AllowedTypes: [])
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("AllowedTypes", ex.Message);
    }
}
