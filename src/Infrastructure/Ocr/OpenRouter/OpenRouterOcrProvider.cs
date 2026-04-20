using System.Net.Http.Json;
using System.Text.Json;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr.LlmVision;
using Microsoft.Extensions.Options;

namespace FinFlow.Infrastructure.Ocr.OpenRouter;

public sealed class OpenRouterOcrProvider : IOcrProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IPdfPageRenderer _pdfPageRenderer;
    private readonly OpenRouterProviderOptions _options;

    public OpenRouterOcrProvider(
        HttpClient httpClient,
        IPdfPageRenderer pdfPageRenderer,
        IOptions<OpenRouterProviderOptions> options)
    {
        _httpClient = httpClient;
        _pdfPageRenderer = pdfPageRenderer;
        _options = options.Value;
    }

    public string Name => "OpenRouter";

    public async Task<Result<OcrExtractionResult>> ExtractAsync(
        string fileName,
        string contentType,
        byte[] fileContents,
        CancellationToken cancellationToken)
    {
        var imagesResult = await LlmVisionImagePreparer.PrepareAsync(
            contentType,
            fileContents,
            _options.MaxImageBytes,
            _options.MaxPagesPerDocument,
            _options.MaxImagesPerRequest,
            _pdfPageRenderer,
            cancellationToken);
        if (imagesResult.IsFailure)
            return Result.Failure<OcrExtractionResult>(imagesResult.Error);

        var request = BuildRequest(imagesResult.Value);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("chat/completions", request, SerializerOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrExtractionFailed);

            var completion = await response.Content.ReadFromJsonAsync<OpenRouterChatCompletionResponse>(SerializerOptions, cancellationToken);
            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);

            return LlmVisionOcrParser.Parse(content, "openrouter");
        }
        catch (HttpRequestException)
        {
            return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrProviderUnavailable);
        }
        catch (TaskCanceledException)
        {
            return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrProviderUnavailable);
        }
    }

    private OpenRouterChatCompletionRequest BuildRequest(IReadOnlyList<OcrPageImage> images)
    {
        var userContent = new List<object>
        {
            new OpenRouterTextContentPart(
                "text",
                LlmVisionPrompt.ExtractExpenseDocument
            )
        };

        userContent.AddRange(images.Select(image =>
            (object)new OpenRouterImageContentPart(
                "image_url",
                new OpenRouterImageUrl($"data:{image.ContentType};base64,{image.Base64Content}"))));

        return new OpenRouterChatCompletionRequest(
            _options.Model,
            [
                new OpenRouterChatMessage("user", userContent)
            ]);
    }
}

internal sealed record OpenRouterChatCompletionRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("model")] string Model,
    [property: System.Text.Json.Serialization.JsonPropertyName("messages")] IReadOnlyList<OpenRouterChatMessage> Messages,
    [property: System.Text.Json.Serialization.JsonPropertyName("temperature")] int Temperature = 0);

internal sealed record OpenRouterChatMessage(
    [property: System.Text.Json.Serialization.JsonPropertyName("role")] string Role,
    [property: System.Text.Json.Serialization.JsonPropertyName("content")] object Content);

internal sealed record OpenRouterTextContentPart(
    [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
    [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text);

internal sealed record OpenRouterImageContentPart(
    [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
    [property: System.Text.Json.Serialization.JsonPropertyName("image_url")] OpenRouterImageUrl ImageUrl);

internal sealed record OpenRouterImageUrl(
    [property: System.Text.Json.Serialization.JsonPropertyName("url")] string Url);

internal sealed record OpenRouterChatCompletionResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("choices")] IReadOnlyList<OpenRouterChatChoice>? Choices);

internal sealed record OpenRouterChatChoice(
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] OpenRouterChatResponseMessage? Message);

internal sealed record OpenRouterChatResponseMessage(
    [property: System.Text.Json.Serialization.JsonPropertyName("content")] string? Content);
