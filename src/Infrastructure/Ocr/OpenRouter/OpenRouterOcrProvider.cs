using System.Net.Http.Json;
using System.Text.Json;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr.LlmVision;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace FinFlow.Infrastructure.Ocr.OpenRouter;

public sealed class OpenRouterOcrProvider : IOcrProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;
    private readonly HttpClient _httpClient;
    private readonly IPdfPageRenderer _pdfPageRenderer;
    private readonly OpenRouterProviderOptions _options;
    private readonly ILogger<OpenRouterOcrProvider>? _logger;
    private readonly Uri _chatCompletionsUri;

    public OpenRouterOcrProvider(
        HttpClient httpClient,
        IPdfPageRenderer pdfPageRenderer,
        IOptions<OpenRouterProviderOptions> options,
        ILogger<OpenRouterOcrProvider>? logger = null)
    {
        _httpClient = httpClient;
        _pdfPageRenderer = pdfPageRenderer;
        _options = options.Value;
        _logger = logger;
        _chatCompletionsUri = BuildChatCompletionsUri(_options.BaseUrl);

        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = args =>
                {
                    // Don't retry if caller already cancelled — avoid extra upstream billing.
                    if (args.Context.CancellationToken.IsCancellationRequested)
                        return ValueTask.FromResult(false);

                    if (args.Outcome.Exception is HttpRequestException)
                        return ValueTask.FromResult(true);

                    if (args.Outcome.Exception is TaskCanceledException tce
                        && tce.InnerException is TimeoutException)
                        return ValueTask.FromResult(true);

                    if (args.Outcome.Result is { IsSuccessStatusCode: false } response
                        && (int)response.StatusCode >= 500)
                        return ValueTask.FromResult(true);

                    return ValueTask.FromResult(false);
                }
            })
            .Build();
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

        var processedPageCount = imagesResult.Value.Pages.Count;
        var wasTruncated = imagesResult.Value.WasTruncated;
        var request = BuildRequest(imagesResult.Value.Pages);

        try
        {
            using var response = await _retryPipeline.ExecuteAsync(
                async ct => await _httpClient.PostAsJsonAsync(_chatCompletionsUri, request, SerializerOptions, ct),
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning(
                    "OpenRouter OCR request failed with HTTP {StatusCode}. Body: {Body}",
                    (int)response.StatusCode,
                    Truncate(responseBody));
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrExtractionFailed);
            }

            if (TryReadOpenRouterError(responseBody, out var upstreamError))
            {
                _logger?.LogWarning(
                    "OpenRouter OCR upstream returned an error payload. Body: {Body}",
                    Truncate(upstreamError));
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrExtractionFailed);
            }

            var completion = JsonSerializer.Deserialize<OpenRouterChatCompletionResponse>(responseBody, SerializerOptions);
            var choice = completion?.Choices?.FirstOrDefault();
            if (choice?.Message is null)
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);

            var content = choice.Message.Content;
            if (string.IsNullOrWhiteSpace(content))
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);

            var parseResult = LlmVisionOcrParser.Parse(content, "openrouter");
            if (parseResult.IsFailure)
            {
                _logger?.LogWarning("OpenRouter OCR response could not be parsed.");
                return parseResult;
            }

            return Result.Success(OcrExtractionResult.Create(
                parseResult.Value.VendorName,
                parseResult.Value.Reference,
                parseResult.Value.DocumentDate,
                parseResult.Value.ExtractedInvoiceDueDate,
                parseResult.Value.Category,
                parseResult.Value.VendorTaxId,
                parseResult.Value.Subtotal,
                parseResult.Value.Vat,
                parseResult.Value.TotalAmount,
                parseResult.Value.Source,
                parseResult.Value.ConfidenceLabel,
                parseResult.Value.LineItems,
                processedPageCount,
                wasTruncated,
                parseResult.Value.CurrencyCode,
                parseResult.Value.TaxLines));
        }
        catch (HttpRequestException)
        {
            return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrProviderUnavailable);
        }
        catch (TaskCanceledException)
        {
            return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrProviderUnavailable);
        }
        catch (System.Text.Json.JsonException)
        {
            return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);
        }
    }

    public async Task<Result<int>> GetPageCountAsync(
        string contentType,
        byte[] fileContents,
        CancellationToken cancellationToken)
    {
        if (fileContents.Length == 0)
            return Result.Failure<int>(DocumentOcrErrors.OcrFileEmpty);

        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return Result.Success(1);

        return await _pdfPageRenderer.GetPageCountAsync(fileContents, cancellationToken);
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

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? throw new InvalidOperationException("OpenRouter OCR base URL is not configured.")
            : baseUrl.TrimEnd('/') + "/";

        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), "chat/completions");
    }

    private static bool TryReadOpenRouterError(string responseBody, out string upstreamError)
    {
        upstreamError = string.Empty;
        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("error", out var errorElement))
                return false;

            upstreamError = errorElement.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value)
    {
        const int maxLength = 1_000;
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}

internal sealed record OpenRouterChatCompletionRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("model")] string Model,
    [property: System.Text.Json.Serialization.JsonPropertyName("messages")] IReadOnlyList<OpenRouterChatMessage> Messages,
    [property: System.Text.Json.Serialization.JsonPropertyName("temperature")] float Temperature = 0f);

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
