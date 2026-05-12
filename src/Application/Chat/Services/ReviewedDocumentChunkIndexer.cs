using System.Text;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Chat.Services;

public sealed class ReviewedDocumentChunkIndexer : IReviewedDocumentChunkIndexer
{
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

        var expenseChunks = await _chunkingService.ChunkAsync(
            document.IdTenant,
            BuildExpenseContent(document),
            DocumentChunkType.Expense,
            document.Id,
            document.MembershipId,
            document.IdDepartment,
            ct: cancellationToken);

        var receiptChunks = await _chunkingService.ChunkAsync(
            document.IdTenant,
            BuildReceiptContent(document),
            DocumentChunkType.Receipt,
            document.Id,
            document.MembershipId,
            document.IdDepartment,
            ct: cancellationToken);

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
        builder.AppendLine($"Merchant: {document.VendorName}");
        builder.AppendLine($"Reference: {document.Reference}");
        builder.AppendLine($"Expense date: {document.DocumentDate:yyyy-MM-dd}");
        builder.AppendLine($"Category: {document.Category}");
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
                    $"- {lineItem.ItemName}: quantity {lineItem.Quantity:0.##}, unit price {lineItem.UnitPrice:0.##}, total {lineItem.Total:0.##}");
            }
        }

        return builder.ToString().Trim();
    }

    internal static string BuildReceiptContent(ReviewedDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Receipt record");
        builder.AppendLine($"Original file name: {document.OriginalFileName}");
        builder.AppendLine($"Content type: {document.ContentType}");
        builder.AppendLine($"Merchant: {document.VendorName}");
        builder.AppendLine($"Reference: {document.Reference}");
        builder.AppendLine($"Document date: {document.DocumentDate:yyyy-MM-dd}");
        builder.AppendLine($"Vendor tax id: {document.VendorTaxId ?? "n/a"}");
        builder.AppendLine($"Source: {document.Source}");
        builder.AppendLine($"Reviewed by staff: {document.ReviewedByStaff}");
        builder.AppendLine($"Confidence label: {document.ConfidenceLabel}");

        if (document.LineItems.Count > 0)
        {
            builder.AppendLine("Receipt line items:");
            foreach (var lineItem in document.LineItems)
            {
                builder.AppendLine(
                    $"- {lineItem.ItemName}: quantity {lineItem.Quantity:0.##}, unit price {lineItem.UnitPrice:0.##}, total {lineItem.Total:0.##}");
            }
        }

        return builder.ToString().Trim();
    }
}
