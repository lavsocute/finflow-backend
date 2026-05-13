using System.Text;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Chat.Services;

public sealed class ReviewedDocumentChunkIndexer : IReviewedDocumentChunkIndexer
{
    private static readonly string[] InstructionLabels = ["SYSTEM:", "ASSISTANT:", "USER:", "DEVELOPER:", "TOOL:"];

    private readonly IChunkingService _chunkingService;
    private readonly IVectorStore _vectorStore;

    public ReviewedDocumentChunkIndexer(
        IChunkingService chunkingService,
        IVectorStore vectorStore)
    {
        _chunkingService = chunkingService;
        _vectorStore = vectorStore;
    }

    public async Task<int> ReindexAsync(ReviewedDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocumentMetadata(document);

        var expenseChunks = await _chunkingService.ChunkAsync(
            document.IdTenant,
            BuildExpenseContent(document),
            DocumentChunkType.Expense,
            document.Id,
            document.MembershipId,
            document.IdDepartment,
            ct: cancellationToken);
        ValidateGeneratedChunks(document, expenseChunks, DocumentChunkType.Expense);

        var receiptChunks = await _chunkingService.ChunkAsync(
            document.IdTenant,
            BuildReceiptContent(document),
            DocumentChunkType.Receipt,
            document.Id,
            document.MembershipId,
            document.IdDepartment,
            ct: cancellationToken);
        ValidateGeneratedChunks(document, receiptChunks, DocumentChunkType.Receipt);

        var chunks = expenseChunks
            .Concat(receiptChunks)
            .ToList();

        await _vectorStore.DeleteByDocumentIdAsync(document.Id, cancellationToken);

        if (chunks.Count == 0)
            return 0;

        await _vectorStore.UpsertChunksAsync(chunks, cancellationToken);
        return chunks.Count;
    }

    internal static string BuildExpenseContent(ReviewedDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Expense record");
        builder.AppendLine($"Merchant: {NormalizeForEvidence(document.VendorName)}");
        builder.AppendLine($"Reference: {NormalizeForEvidence(document.Reference)}");
        builder.AppendLine($"Expense date: {document.DocumentDate:yyyy-MM-dd}");
        builder.AppendLine($"Category: {NormalizeForEvidence(document.Category)}");
        builder.AppendLine($"DepartmentId: {document.IdDepartment}");
        builder.AppendLine($"Subtotal: {document.Subtotal:0.##}");
        builder.AppendLine($"VAT: {document.Vat:0.##}");
        builder.AppendLine($"Total: {document.TotalAmount:0.##}");
        builder.AppendLine($"Status: {document.Status}");
        builder.AppendLine($"Submitted at UTC: {document.SubmittedAt:O}");

        if (document.LineItems.Count > 0)
        {
            builder.AppendLine("Line items:");
            foreach (var lineItem in document.LineItems)
            {
                builder.AppendLine(
                    $"- {NormalizeForEvidence(lineItem.ItemName)}: quantity {lineItem.Quantity:0.##}, unit price {lineItem.UnitPrice:0.##}, total {lineItem.Total:0.##}");
            }
        }

        return builder.ToString().Trim();
    }

    internal static string BuildReceiptContent(ReviewedDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Receipt record");
        builder.AppendLine($"Original file name: {NormalizeForEvidence(document.OriginalFileName)}");
        builder.AppendLine($"Content type: {NormalizeForEvidence(document.ContentType)}");
        builder.AppendLine($"Merchant: {NormalizeForEvidence(document.VendorName)}");
        builder.AppendLine($"Reference: {NormalizeForEvidence(document.Reference)}");
        builder.AppendLine($"Document date: {document.DocumentDate:yyyy-MM-dd}");
        builder.AppendLine($"Vendor tax id: {NormalizeForEvidence(document.VendorTaxId ?? "n/a")}");
        builder.AppendLine($"Source: {NormalizeForEvidence(document.Source)}");
        builder.AppendLine($"Reviewed by staff: {NormalizeForEvidence(document.ReviewedByStaff)}");
        builder.AppendLine($"Confidence label: {NormalizeForEvidence(document.ConfidenceLabel)}");

        if (document.LineItems.Count > 0)
        {
            builder.AppendLine("Receipt line items:");
            foreach (var lineItem in document.LineItems)
            {
                builder.AppendLine(
                    $"- {NormalizeForEvidence(lineItem.ItemName)}: quantity {lineItem.Quantity:0.##}, unit price {lineItem.UnitPrice:0.##}, total {lineItem.Total:0.##}");
            }
        }

        return builder.ToString().Trim();
    }

    private static void ValidateDocumentMetadata(ReviewedDocument document)
    {
        if (document.Id == Guid.Empty)
            throw new ArgumentException("Reviewed document id is required.", nameof(document));
        if (document.IdTenant == Guid.Empty)
            throw new ArgumentException("Reviewed document tenant is required.", nameof(document));
        if (document.IdDepartment == Guid.Empty)
            throw new ArgumentException("Reviewed document department is required.", nameof(document));
        if (document.MembershipId == Guid.Empty)
            throw new ArgumentException("Reviewed document owner membership is required.", nameof(document));
    }

    private static void ValidateGeneratedChunks(
        ReviewedDocument document,
        IReadOnlyCollection<DocumentChunk> chunks,
        DocumentChunkType expectedType)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Type != expectedType)
                throw new InvalidOperationException($"Generated chunk type drift detected. Expected {expectedType} but received {chunk.Type}.");
            if (chunk.IdTenant != document.IdTenant)
                throw new InvalidOperationException("Generated chunk tenant metadata must match the reviewed document tenant.");
            if (chunk.OwnerMembershipId != document.MembershipId)
                throw new InvalidOperationException("Generated chunk owner metadata must match the reviewed document owner.");
            if (chunk.DocumentId != document.Id)
                throw new InvalidOperationException("Generated chunk document metadata must match the reviewed document id.");
            if (chunk.DepartmentId != document.IdDepartment)
                throw new InvalidOperationException("Generated chunk department metadata must match the reviewed document department.");
            if (string.IsNullOrWhiteSpace(chunk.Content))
                throw new InvalidOperationException("Generated chunk content is required.");
            if (string.IsNullOrWhiteSpace(chunk.ContentHash))
                throw new InvalidOperationException("Generated chunk content hash is required.");
            if (ContainsInstructionLikeLabel(chunk.Content))
                throw new InvalidOperationException("Generated chunk content still contains instruction-like labels.");
        }
    }

    private static string NormalizeForEvidence(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;

        foreach (var label in InstructionLabels)
        {
            normalized = normalized.Replace(
                label,
                $"{char.ToUpperInvariant(label[0])}{label[1..^1].ToLowerInvariant()} label:",
                StringComparison.OrdinalIgnoreCase);
        }

        return normalized;
    }

    private static bool ContainsInstructionLikeLabel(string value) =>
        InstructionLabels.Any(label => value.Contains(label, StringComparison.OrdinalIgnoreCase));
}
