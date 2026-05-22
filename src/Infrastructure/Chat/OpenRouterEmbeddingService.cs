using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FinFlow.Infrastructure.Chat;

public class OpenRouterEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenRouterEmbeddingService> _logger;
    private readonly OpenRouterEmbeddingOptions _options;
    private readonly Uri _embeddingsUri;

    public OpenRouterEmbeddingService(
        HttpClient httpClient,
        Microsoft.Extensions.Options.IOptions<OpenRouterEmbeddingOptions> options,
        ILogger<OpenRouterEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _embeddingsUri = BuildEmbeddingsUri(_options.BaseUrl);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var request = new
        {
            model = _options.Model,
            input = text
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_embeddingsUri, content, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<OpenRouterEmbeddingResponse>(ct);

            if (json?.Data == null || json.Data.Count == 0)
                throw new InvalidOperationException("No embedding returned from OpenRouter");

            var embedding = json.Data[0].Embedding;

            if (embedding.Length != _options.ExpectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch. Expected {_options.ExpectedDimensions} but provider returned {embedding.Length}.");
            }

            return embedding;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "OpenRouter embedding request timed out after {TimeoutSeconds}s", _options.RequestTimeoutSeconds);
            throw new InvalidOperationException("AI search is temporarily unavailable because the embedding request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding via OpenRouter");
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

    private record OpenRouterEmbeddingResponse(
        List<OpenRouterEmbeddingData> Data
    );

    private record OpenRouterEmbeddingData(
        float[] Embedding
    );

    private static Uri BuildEmbeddingsUri(string baseUrl)
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? throw new InvalidOperationException("OpenRouter embedding base URL is not configured.")
            : baseUrl.TrimEnd('/') + "/";

        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), "embeddings");
    }
}
