namespace FinFlow.Application.Chat.Services;

public static class ChatCompletionsEndpointBuilder
{
    public static Uri Build(string? baseUrl, string defaultBaseUrl = "https://api.groq.com/openai/v1")
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? defaultBaseUrl
            : baseUrl.Trim();

        normalizedBaseUrl = normalizedBaseUrl.TrimEnd('/') + "/";

        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), "chat/completions");
    }
}
