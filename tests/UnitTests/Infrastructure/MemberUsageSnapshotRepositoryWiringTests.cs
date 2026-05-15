using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FinFlow.Application.Common.Abstractions;
using Xunit;

namespace FinFlow.UnitTests.Infrastructure;

public sealed class MemberUsageSnapshotRepositoryWiringTests
{
    [Fact]
    public void AddInfrastructure_RegistersMemberUsageSnapshotRepository()
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
        var repository = provider.GetRequiredService<IMemberUsageSnapshotRepository>();

        Assert.NotNull(repository);
    }

    [Fact]
    public void AddInfrastructure_RegistersMemberUsageService()
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
        var service = provider.GetRequiredService<IMemberUsageService>();

        Assert.NotNull(service);
    }
}
