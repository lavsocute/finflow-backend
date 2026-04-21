using FinFlow.Domain.TenantSubscriptions;
using FinFlow.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.UnitTests.Infrastructure;

public sealed class TenantSubscriptionRepositoryWiringTests
{
    [Fact]
    public void AddInfrastructure_RegistersTenantSubscriptionRepository()
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
        var repository = provider.GetRequiredService<ITenantSubscriptionRepository>();

        Assert.NotNull(repository);
    }
}
