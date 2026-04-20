using System.Net.Http.Json;
using System.Text.Json;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
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
        var imagesResult = await PrepareImagesAsync(contentType, fileContents, cancellationToken);
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

            return ParseContent(content);
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

    private async Task<Result<IReadOnlyList<OcrPageImage>>> PrepareImagesAsync(
        string contentType,
        byte[] fileContents,
        CancellationToken cancellationToken)
    {
        if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            var renderResult = await _pdfPageRenderer.RenderAsync(fileContents, cancellationToken);
            if (renderResult.IsFailure)
                return Result.Failure<IReadOnlyList<OcrPageImage>>(renderResult.Error);

            var renderedPages = renderResult.Value;
            if (renderedPages.Count == 0)
                return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrPdfRenderFailed);
            if (renderedPages.Count > _options.MaxImagesPerRequest)
                return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrExtractionFailed);

            foreach (var page in renderedPages)
            {
                if (GetDecodedByteLength(page.Base64Content) > _options.MaxImageBytes)
                    return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrFileTooLarge);
            }

            return Result.Success<IReadOnlyList<OcrPageImage>>(renderedPages);
        }

        if (!IsSupportedImage(contentType))
            return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrUnsupportedFile);

        if (fileContents.Length > _options.MaxImageBytes)
            return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrFileTooLarge);

        return Result.Success<IReadOnlyList<OcrPageImage>>(
            [
                new OcrPageImage(1, contentType, Convert.ToBase64String(fileContents))
            ]);
    }

    private OpenRouterChatCompletionRequest BuildRequest(IReadOnlyList<OcrPageImage> images)
    {
        var userContent = new List<object>
        {
            new OpenRouterTextContentPart(
                "text",
                """
                Extract this expense document into strict JSON only.
                Return keys:
                vendorName, reference, documentDate, dueDate, category, vendorTaxId, subtotal, vat, totalAmount, lineItems.
                lineItems must be an array of objects with itemName, quantity, unitPrice, total.
                Rules:
                - Output JSON only
                - Dates must use yyyy-MM-dd
                - Use numbers for money and quantities
                - Do not invent values
                """
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

    private static Result<OcrExtractionResult> ParseContent(string content)
    {
        var normalizedContent = StripCodeFences(content);

        try
        {
            using var document = JsonDocument.Parse(normalizedContent);
            var root = document.RootElement;

            var vendorName = GetRequiredString(root, "vendorName");
            var reference = GetRequiredString(root, "reference");
            var documentDate = GetRequiredDate(root, "documentDate");
            var dueDate = GetRequiredDate(root, "dueDate");
            var category = GetRequiredString(root, "category");
            var vendorTaxId = GetOptionalString(root, "vendorTaxId");
            var subtotal = GetRequiredDecimal(root, "subtotal");
            var vat = GetRequiredDecimal(root, "vat");
            var totalAmount = GetRequiredDecimal(root, "totalAmount");

            if (!root.TryGetProperty("lineItems", out var lineItemsElement) || lineItemsElement.ValueKind != JsonValueKind.Array)
                return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);

            var lineItems = new List<OcrExtractionLineItem>();
            foreach (var item in lineItemsElement.EnumerateArray())
            {
                lineItems.Add(new OcrExtractionLineItem(
                    GetRequiredString(item, "itemName"),
                    GetRequiredDecimal(item, "quantity"),
                    GetRequiredDecimal(item, "unitPrice"),
                    GetRequiredDecimal(item, "total")));
            }

            return Result.Success(new OcrExtractionResult(
                vendorName,
                reference,
                documentDate,
                dueDate,
                category,
                vendorTaxId,
                subtotal,
                vat,
                totalAmount,
                "openrouter",
                "AI extracted",
                lineItems));
        }
        catch (FormatException)
        {
            return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);
        }
        catch (JsonException)
        {
            return Result.Failure<OcrExtractionResult>(DocumentOcrErrors.OcrInvalidJson);
        }
    }

    private static string StripCodeFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var lines = trimmed.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            .ToArray();

        return string.Join('\n', lines).Trim();
    }

    private static bool IsSupportedImage(string contentType) =>
        string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase);

    private static int GetDecodedByteLength(string base64Content) =>
        Convert.FromBase64String(base64Content).Length;

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var value = GetOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{propertyName} is required.");

        return value;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => property.GetString()?.Trim(),
            _ => throw new InvalidOperationException($"{propertyName} must be a string.")
        };
    }

    private static DateOnly GetRequiredDate(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (!DateOnly.TryParse(value, out var date))
            throw new FormatException($"{propertyName} must be a valid date.");

        return date;
    }

    private static decimal GetRequiredDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            throw new InvalidOperationException($"{propertyName} is required.");

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDecimal(out var value) => value,
            _ => throw new InvalidOperationException($"{propertyName} must be a decimal.")
        };
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
