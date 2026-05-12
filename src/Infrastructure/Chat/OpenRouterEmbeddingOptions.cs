namespace FinFlow.Infrastructure.Chat;

public sealed class OpenRouterEmbeddingOptions
{
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "nvidia/llama-nemotron-embed-vl-1b-v2:free";
    public int ExpectedDimensions { get; init; } = 2048;
    public int RequestTimeoutSeconds { get; init; } = 30;
}
