using System.Net;

namespace FinFlow.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_Endpoint_ReturnsHealthyStatus()
    {
        await using var factory = new GraphQlApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthReady_Endpoint_ReturnsHealthyStatus()
    {
        await using var factory = new GraphQlApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database", body, StringComparison.OrdinalIgnoreCase);
    }
}
