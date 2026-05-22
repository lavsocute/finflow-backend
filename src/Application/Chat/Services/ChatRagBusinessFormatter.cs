using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Documents;
using System.Globalization;
using System.Text;

namespace FinFlow.Application.Chat.Services;

internal static class ChatRagBusinessFormatter
{
    public const string FormatVersion = "ragfmt-2026.05.1";
    private const int MaxRecentItems = 3;
    private const int MaxDetailLineItems = 3;

    public static RagBusinessFormatResult? TryFormat(string query, IReadOnlyList<DocumentChunk> chunks)
    {
        if (string.IsNullOrWhiteSpace(query) || chunks.Count == 0)
            return null;

        var normalizedQuery = IntentTextNormalizer.Normalize(query);
        var intent = ResolveIntent(normalizedQuery);
        if (intent == RagBusinessFormatIntent.None)
            return null;

        var documents = chunks
            .Where(static chunk => chunk.Type is DocumentChunkType.Expense or DocumentChunkType.Receipt or DocumentChunkType.LineItem)
            .GroupBy(static chunk => chunk.DocumentId)
            .Select(ParseDocumentGroup)
            .Where(static document => document.HasBusinessContent)
            .OrderByDescending(static document => document.SubmittedAtUtc)
            .ThenByDescending(static document => document.DocumentDate)
            .ThenByDescending(static document => document.TotalAmount)
            .ToList();

        if (documents.Count == 0)
            return null;

        if (intent == RagBusinessFormatIntent.Recent)
            documents = documents.Take(MaxRecentItems).ToList();

        if (intent == RagBusinessFormatIntent.Detail)
            documents = [documents[0]];

        var answer = intent == RagBusinessFormatIntent.Detail
            ? BuildDetailAnswer(documents[0])
            : BuildListAnswer(documents, intent, normalizedQuery);

        var citations = documents
            .Select((document, index) => new ChatCitation(
                index + 1,
                document.RepresentativeChunk.Id,
                document.DocumentId,
                document.RepresentativeChunk.Type.ToString(),
                BuildPreview(document.RepresentativeChunk.Content)))
            .ToList();

        return new RagBusinessFormatResult(answer, citations, documents.Count);
    }

    private static ParsedRagDocument ParseDocumentGroup(IGrouping<Guid, DocumentChunk> group)
    {
        var parsed = new ParsedRagDocument(
            group.Key,
            SelectRepresentativeChunk(group),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            []);

        foreach (var chunk in group)
        {
            parsed = Merge(parsed, ParseChunk(chunk));
        }

        return parsed;
    }

    private static ParsedRagDocument ParseChunk(DocumentChunk chunk)
    {
        string? supplier = null;
        string? reference = null;
        DateOnly? documentDate = null;
        string? category = null;
        decimal? totalAmount = null;
        string? status = null;
        DateTime? submittedAtUtc = null;
        string? fileName = null;
        var lineItems = new List<string>();

        var lines = chunk.Content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var lineItemName = ExtractLineItemName(line);
                if (!string.IsNullOrWhiteSpace(lineItemName))
                    lineItems.Add(lineItemName);
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
                continue;

            var label = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (IsIgnorableValue(value))
                continue;

            switch (label)
            {
                case "Merchant":
                case "Vendor":
                    supplier ??= NormalizeSupplier(value);
                    break;
                case "Reference":
                case "Document reference":
                    reference ??= NormalizeReference(value);
                    break;
                case "Expense date":
                case "Document date":
                    documentDate ??= ParseDate(value);
                    break;
                case "Category":
                    category ??= NormalizeCategory(value);
                    break;
                case "Total":
                    totalAmount ??= ParseDecimal(value);
                    break;
                case "Status":
                    status ??= value;
                    break;
                case "Submitted at UTC":
                    submittedAtUtc ??= ParseDateTime(value);
                    break;
                case "Original file name":
                    fileName ??= DocumentTextNormalizer.NormalizeEvidenceValue(value);
                    break;
                case "Item name":
                    var itemName = DocumentTextNormalizer.NormalizeLineItemName(value);
                    if (!string.IsNullOrWhiteSpace(itemName))
                        lineItems.Add(itemName);
                    break;
            }
        }

        return new ParsedRagDocument(
            chunk.DocumentId,
            chunk,
            supplier,
            reference,
            documentDate,
            category,
            totalAmount,
            status,
            submittedAtUtc,
            DeduplicateLineItems(lineItems));
    }

    private static ParsedRagDocument Merge(ParsedRagDocument current, ParsedRagDocument incoming)
    {
        var mergedLineItems = current.LineItems
            .Concat(incoming.LineItems)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxDetailLineItems)
            .ToList();

        return current with
        {
            Supplier = current.Supplier ?? incoming.Supplier,
            Reference = current.Reference ?? incoming.Reference,
            DocumentDate = current.DocumentDate ?? incoming.DocumentDate,
            Category = current.Category ?? incoming.Category,
            TotalAmount = current.TotalAmount ?? incoming.TotalAmount,
            Status = current.Status ?? incoming.Status,
            SubmittedAtUtc = current.SubmittedAtUtc ?? incoming.SubmittedAtUtc,
            LineItems = mergedLineItems
        };
    }

    private static string BuildListAnswer(IReadOnlyList<ParsedRagDocument> documents, RagBusinessFormatIntent intent, string normalizedQuery)
    {
        var subject = ResolveSubjectLabel(normalizedQuery);
        var intro = intent == RagBusinessFormatIntent.Recent
            ? $"Tôi tìm thấy {documents.Count} {subject} gần đây trong phạm vi bạn được phép xem."
            : $"Tôi tìm thấy {documents.Count} {subject} phù hợp trong phạm vi bạn được phép xem.";

        var lines = new List<string> { intro, string.Empty };

        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            lines.Add($"{index + 1}. {ResolveHeading(document)}");

            if (!string.IsNullOrWhiteSpace(document.Reference))
                lines.Add($"- Mã tham chiếu: {document.Reference}");

            if (document.DocumentDate.HasValue)
                lines.Add($"- Ngày chứng từ: {document.DocumentDate:yyyy-MM-dd}");

            if (!string.IsNullOrWhiteSpace(document.Category))
                lines.Add($"- Hạng mục: {document.Category}");

            if (document.TotalAmount.HasValue)
                lines.Add($"- Tổng tiền: {FormatAmount(document.TotalAmount.Value)} VND");

            if (!string.IsNullOrWhiteSpace(document.Status))
                lines.Add($"- Trạng thái: {MapStatus(document.Status)}");

            if (index < documents.Count - 1)
                lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static string BuildDetailAnswer(ParsedRagDocument document)
    {
        var lines = new List<string>
        {
            "Đây là thông tin chứng từ phù hợp nhất tôi tìm thấy:"
        };

        if (!string.IsNullOrWhiteSpace(document.Supplier))
            lines.Add($"- Nhà cung cấp: {document.Supplier}");

        if (!string.IsNullOrWhiteSpace(document.Reference))
            lines.Add($"- Mã tham chiếu: {document.Reference}");

        if (document.DocumentDate.HasValue)
            lines.Add($"- Ngày chứng từ: {document.DocumentDate:yyyy-MM-dd}");

        if (!string.IsNullOrWhiteSpace(document.Category))
            lines.Add($"- Hạng mục: {document.Category}");

        if (document.TotalAmount.HasValue)
            lines.Add($"- Tổng tiền: {FormatAmount(document.TotalAmount.Value)} VND");

        if (!string.IsNullOrWhiteSpace(document.Status))
            lines.Add($"- Trạng thái: {MapStatus(document.Status)}");

        if (document.LineItems.Count > 0)
            lines.Add($"- Mặt hàng nổi bật: {string.Join(", ", document.LineItems)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static RagBusinessFormatIntent ResolveIntent(string normalizedQuery)
    {
        if (!MentionsExpenseOrDocument(normalizedQuery))
            return RagBusinessFormatIntent.None;

        if (normalizedQuery.Contains("gan day", StringComparison.Ordinal) ||
            normalizedQuery.Contains("recent", StringComparison.Ordinal) ||
            normalizedQuery.Contains("moi nhat", StringComparison.Ordinal))
        {
            return RagBusinessFormatIntent.Recent;
        }

        if (normalizedQuery.Contains("chi tiet", StringComparison.Ordinal) ||
            normalizedQuery.Contains("thong tin", StringComparison.Ordinal) ||
            normalizedQuery.Contains("noi dung", StringComparison.Ordinal))
        {
            return RagBusinessFormatIntent.Detail;
        }

        if (normalizedQuery.Contains("show", StringComparison.Ordinal) ||
            normalizedQuery.Contains("list", StringComparison.Ordinal) ||
            normalizedQuery.Contains("liet ke", StringComparison.Ordinal) ||
            normalizedQuery.Contains("danh sach", StringComparison.Ordinal) ||
            normalizedQuery.Contains("tat ca", StringComparison.Ordinal) ||
            normalizedQuery.Contains("hien thi", StringComparison.Ordinal) ||
            normalizedQuery.Contains("xem", StringComparison.Ordinal))
        {
            return RagBusinessFormatIntent.List;
        }

        return RagBusinessFormatIntent.None;
    }

    private static bool MentionsExpenseOrDocument(string normalizedQuery) =>
        normalizedQuery.Contains("expense", StringComparison.Ordinal) ||
        normalizedQuery.Contains("chi phi", StringComparison.Ordinal) ||
        normalizedQuery.Contains("hoa don", StringComparison.Ordinal) ||
        normalizedQuery.Contains("chung tu", StringComparison.Ordinal) ||
        normalizedQuery.Contains("receipt", StringComparison.Ordinal) ||
        normalizedQuery.Contains("bill", StringComparison.Ordinal);

    private static string ResolveSubjectLabel(string normalizedQuery)
    {
        if (normalizedQuery.Contains("hoa don", StringComparison.Ordinal) ||
            normalizedQuery.Contains("receipt", StringComparison.Ordinal) ||
            normalizedQuery.Contains("bill", StringComparison.Ordinal))
        {
            return "chứng từ";
        }

        return "khoản chi";
    }

    private static DocumentChunk SelectRepresentativeChunk(IEnumerable<DocumentChunk> chunks) =>
        chunks
            .OrderBy(static chunk => chunk.Type switch
            {
                DocumentChunkType.Expense => 0,
                DocumentChunkType.Receipt => 1,
                DocumentChunkType.LineItem => 2,
                _ => 3
            })
            .ThenBy(static chunk => chunk.ChunkIndex)
            .First();

    private static string ResolveHeading(ParsedRagDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.Supplier))
            return document.Supplier;

        if (!string.IsNullOrWhiteSpace(document.Reference))
            return $"Chứng từ {document.Reference}";

        return "Chứng từ chưa có tên";
    }

    private static string NormalizeSupplier(string value)
    {
        var normalized = DocumentTextNormalizer.NormalizeVendorName(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    private static string NormalizeReference(string value)
    {
        var normalized = DocumentTextNormalizer.NormalizeReference(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    private static string NormalizeCategory(string value)
    {
        var normalized = DocumentTextNormalizer.NormalizeCategory(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    private static bool IsIgnorableValue(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value == "00000000-0000-0000-0000-000000000000" ||
        value.Equals("n/a", StringComparison.OrdinalIgnoreCase);

    private static string ExtractLineItemName(string line)
    {
        var value = line[2..].Trim();
        var separatorIndex = value.IndexOf(':');
        if (separatorIndex > 0)
            value = value[..separatorIndex].Trim();

        return DocumentTextNormalizer.NormalizeLineItemName(value);
    }

    private static IReadOnlyList<string> DeduplicateLineItems(IEnumerable<string> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxDetailLineItems)
            .ToList();

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static DateTime? ParseDateTime(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;

    private static string FormatAmount(decimal amount) =>
        amount.ToString("#,##0.##", CultureInfo.InvariantCulture);

    private static string MapStatus(string status) =>
        status.Trim() switch
        {
            "ReadyForApproval" => "Chờ duyệt",
            "Approved" => "Đã duyệt",
            "Rejected" => "Bị từ chối",
            "Draft" => "Nháp",
            "Cancelled" => "Đã hủy",
            _ => status
        };

    private static string BuildPreview(string content) =>
        content.Length <= 100 ? content : content[..100] + "...";

    internal sealed record RagBusinessFormatResult(
        string Answer,
        IReadOnlyList<ChatCitation> Citations,
        int DocumentCount);

    private sealed record ParsedRagDocument(
        Guid DocumentId,
        DocumentChunk RepresentativeChunk,
        string? Supplier,
        string? Reference,
        DateOnly? DocumentDate,
        string? Category,
        decimal? TotalAmount,
        string? Status,
        DateTime? SubmittedAtUtc,
        IReadOnlyList<string> LineItems)
    {
        public bool HasBusinessContent =>
            !string.IsNullOrWhiteSpace(Supplier) ||
            !string.IsNullOrWhiteSpace(Reference) ||
            DocumentDate.HasValue ||
            TotalAmount.HasValue ||
            !string.IsNullOrWhiteSpace(Status);
    }

    private enum RagBusinessFormatIntent
    {
        None,
        List,
        Recent,
        Detail
    }
}
