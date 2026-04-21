using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinFlow.UnitTests.Infrastructure;

public sealed class TenantUsageSnapshotRepositoryWiringTests
{
    [Fact]
    public void AddInfrastructure_RegistersTenantUsageSnapshotRepository()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=finflow-tests;Username=test;Password=test"
            })
            .Build();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<ITenantUsageSnapshotRepository>();

        Assert.NotNull(repository);
    }
}
