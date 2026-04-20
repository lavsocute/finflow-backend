namespace FinFlow.Infrastructure.Ocr.OpenRouter;

public sealed class OpenRouterProviderOptions
{
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1/";
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "google/gemma-4-31b-it:free";
    public int MaxPagesPerDocument { get; init; } = 3;
    public int MaxImagesPerRequest { get; init; } = 5;
    public int MaxImageBytes { get; init; } = 4 * 1024 * 1024;
    public int RequestTimeoutSeconds { get; init; } = 60;
    public string? Referer { get; init; }
    public string? Title { get; init; }
}
