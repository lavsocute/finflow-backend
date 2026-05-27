namespace FinFlow.Application.Chat.Interfaces;

/// <summary>
/// Provider-agnostic LLM chat options.
/// </summary>
public class LlmChatOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string ChatModel { get; set; } = "llama-3.3-70b-versatile";
}