using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr.Groq;
using FinFlow.Infrastructure.Ocr.LlmVision;
using Microsoft.Extensions.Options;

namespace FinFlow.Infrastructure.Ocr.Groq;

public sealed class GroqOcrProvider : IOcrProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IPdfPageRenderer _pdfPageRenderer;
    private readonly GroqProviderOptions _options;

    public GroqOcrProvider(
        HttpClient httpClient,
        IPdfPageRenderer pdfPageRenderer,
        IOptions<GroqProviderOptions> options)
    {
        _httpClient = httpClient;
        _pdfPageRenderer = pdfPageRenderer;
        _options = options.Value;
    }

    public string Name => "Groq";

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

            var completion = await response.Content.ReadFromJsonAsync<GroqChatCompletionResponse>(SerializerOptions, cancellationToken);
            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);

            return LlmVisionOcrParser.Parse(content, "groq");
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

    private GroqChatCompletionRequest BuildRequest(IReadOnlyList<OcrPageImage> images)
    {
        var userContent = new List<object>
        {
            new GroqTextContentPart(
                "text",
                LlmVisionPrompt.ExtractExpenseDocument
            )
        };

        userContent.AddRange(images.Select(image =>
            (object)new GroqImageContentPart(
                "image_url",
                new GroqImageUrl($"data:{image.ContentType};base64,{image.Base64Content}"))));

        return new GroqChatCompletionRequest(
            _options.Model,
            [
                new GroqChatMessage("user", userContent)
            ]);
    }
}
