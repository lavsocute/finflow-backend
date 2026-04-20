using System.Text.Json;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;

namespace FinFlow.Infrastructure.Ocr.LlmVision;

public static class LlmVisionOcrParser
{
    public static Result<OcrExtractionResult> Parse(string content, string source)
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
                source,
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
