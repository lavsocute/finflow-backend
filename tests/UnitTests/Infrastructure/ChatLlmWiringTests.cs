using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.UnitTests.Infrastructure;

public sealed class ChatLlmWiringTests
{
    [Fact]
    public void AddInfrastructure_ResolvesILlmChatServiceThroughConfiguredTypedClient()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(CreateConfiguration());

        var descriptor = services.Last(x => x.ServiceType == typeof(ILlmChatService));
        Assert.NotNull(descriptor.ImplementationFactory);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var abstraction = scope.ServiceProvider.GetRequiredService<ILlmChatService>();

        Assert.IsType<GroqLlmChatService>(abstraction);
    }

    [Fact]
    public void LlmEntityExtractorOptions_UsesChatModel_WhenSpecificModelIsNotConfigured()
    {
        var options = new LlmEntityExtractorOptions
        {
            Model = "",
            ChatModel = "llama-3.3-70b-versatile"
        };

        Assert.Equal("llama-3.3-70b-versatile", options.EffectiveModel);
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5434;Database=finflow_db;Username=postgres;Password=postgres123",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["JwtSettings:Secret"] = "01234567890123456789012345678901",
                ["JwtSettings:Issuer"] = "FinFlow.Tests",
                ["JwtSettings:Audience"] = "FinFlow.Tests",
                ["Chat:BaseUrl"] = "https://api.groq.com/openai/v1",
                ["Chat:ApiKey"] = "test-key",
                ["Chat:ChatModel"] = "llama-3.3-70b-versatile"
            })
            .Build();
}
