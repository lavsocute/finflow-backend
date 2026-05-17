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
using Polly;
using Polly.Retry;

namespace FinFlow.Infrastructure.Ocr.Groq;

public sealed class GroqOcrProvider : IOcrProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;
    private readonly HttpClient _httpClient;
    private readonly IPdfPageRenderer _pdfPageRenderer;
    private readonly GroqProviderOptions _options;
    private readonly Uri _chatCompletionsUri;

    public GroqOcrProvider(
        HttpClient httpClient,
        IPdfPageRenderer pdfPageRenderer,
        IOptions<GroqProviderOptions> options)
    {
        _httpClient = httpClient;
        _pdfPageRenderer = pdfPageRenderer;
        _options = options.Value;
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

        var processedPageCount = imagesResult.Value.Pages.Count;
        var wasTruncated = imagesResult.Value.WasTruncated;
        var request = BuildRequest(imagesResult.Value.Pages);

        try
        {
            using var response = await _retryPipeline.ExecuteAsync(
                async ct => await _httpClient.PostAsJsonAsync(_chatCompletionsUri, request, SerializerOptions, ct),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrExtractionFailed);

            var completion = await response.Content.ReadFromJsonAsync<GroqChatCompletionResponse>(SerializerOptions, cancellationToken);
            var choice = completion?.Choices?.FirstOrDefault();
            if (choice?.Message is null)
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);

            var content = choice.Message.Content;
            if (string.IsNullOrWhiteSpace(content))
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);

            var parseResult = LlmVisionOcrParser.Parse(content, "groq");
            if (parseResult.IsFailure)
                return parseResult;

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
                parseResult.Value.CurrencyCode));
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
            // Fix #27: provider returned 200 OK with non-JSON (HTML captive portal, etc.)
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

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? throw new InvalidOperationException("Groq OCR base URL is not configured.")
            : baseUrl.TrimEnd('/') + "/";

        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), "chat/completions");
    }
}
