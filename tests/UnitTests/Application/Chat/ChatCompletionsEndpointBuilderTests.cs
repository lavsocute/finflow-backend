using FinFlow.Application.Chat.Services;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class ChatCompletionsEndpointBuilderTests
{
    [Theory]
    [InlineData("https://api.groq.com/openai/v1", "https://api.groq.com/openai/v1/chat/completions")]
    [InlineData("https://api.groq.com/openai/v1/", "https://api.groq.com/openai/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/v1/", "https://openrouter.ai/api/v1/chat/completions")]
    public void Build_PreservesVersionPath_WithOrWithoutTrailingSlash(string baseUrl, string expected)
    {
        var endpoint = ChatCompletionsEndpointBuilder.Build(baseUrl);

        Assert.Equal(expected, endpoint.AbsoluteUri);
    }

    [Fact]
    public void Build_UsesDefaultGroqEndpoint_WhenBaseUrlIsBlank()
    {
        var endpoint = ChatCompletionsEndpointBuilder.Build(" ");

        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", endpoint.AbsoluteUri);
    }
}
