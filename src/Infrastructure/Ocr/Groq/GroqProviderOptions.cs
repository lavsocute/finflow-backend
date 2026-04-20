namespace FinFlow.Infrastructure.Ocr.Groq;

public sealed class GroqProviderOptions
{
    public string BaseUrl { get; init; } = "https://api.groq.com/openai/v1";
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "meta-llama/llama-4-scout-17b-16e-instruct";
    public int MaxPagesPerDocument { get; init; } = 3;
    public int MaxImagesPerRequest { get; init; } = 5;
    public int MaxImageBytes { get; init; } = 4 * 1024 * 1024;
    public int RequestTimeoutSeconds { get; init; } = 30;
}
