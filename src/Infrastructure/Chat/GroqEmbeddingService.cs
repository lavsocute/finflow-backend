using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FinFlow.Infrastructure.Chat;

public class GroqEmbeddingOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string EmbeddingModel { get; set; } = "llama-3.1-8b-instruct";
    public int ExpectedDimensions { get; set; } = 2048;
}

public class GroqEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqEmbeddingService> _logger;
    private readonly int _expectedDimensions;

    public GroqEmbeddingService(
        HttpClient httpClient,
        ILogger<GroqEmbeddingService> logger,
        Microsoft.Extensions.Options.IOptions<GroqEmbeddingOptions>? options = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _expectedDimensions = options?.Value.ExpectedDimensions ?? 2048;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var request = new
        {
            model = "llama-3.1-8b-instruct",
            input = text
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("/embeddings", content, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<GroqEmbeddingResponse>(ct);

            if (json?.Data == null || json.Data.Count == 0)
                throw new InvalidOperationException("No embedding returned from Groq");

            var embedding = json.Data[0].Embedding;

            if (embedding.Length != _expectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch. Expected {_expectedDimensions} but provider returned {embedding.Length}.");
            }

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding");
            throw;
        }
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await EmbedAsync(text, ct));
        }
        return results;
    }

    private record GroqEmbeddingResponse(
        List<GroqEmbeddingData> Data
    );

    private record GroqEmbeddingData(
        float[] Embedding
    );
}