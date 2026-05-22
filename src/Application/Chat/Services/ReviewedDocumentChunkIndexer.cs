using System.Text;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Chat.Services;

public sealed class ReviewedDocumentChunkIndexer : IReviewedDocumentChunkIndexer
{
    private static readonly string[] InstructionLabels = ["SYSTEM:", "ASSISTANT:", "USER:", "DEVELOPER:", "TOOL:"];
    private const int ExpectedEmbeddingDimensions = 2048;

    private readonly IChunkingService _chunkingService;
    private readonly IVectorStore _vectorStore;
    private readonly ICacheService? _cacheService;

    public ReviewedDocumentChunkIndexer(
        IChunkingService chunkingService,
        IVectorStore vectorStore,
        ICacheService? cacheService = null)
    {
        _chunkingService = chunkingService;
        _vectorStore = vectorStore;
        _cacheService = cacheService;
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

        // Per-line-item chunks (semantic chunking): each line item gets its own chunk
        // so semantic search can locate "Cloud Compute Instance for ACME" precisely
        // instead of having it diluted into a 500-char generic chunk.
        var lineItemChunks = new List<DocumentChunk>();
        foreach (var lineItem in document.LineItems)
        {
            var content = BuildLineItemContent(document, lineItem);
            var chunks = await _chunkingService.ChunkAsync(
                document.IdTenant,
                content,
                DocumentChunkType.LineItem,
                document.Id,
                document.MembershipId,
                document.IdDepartment,
                ct: cancellationToken);
            ValidateGeneratedChunks(document, chunks, DocumentChunkType.LineItem);
            lineItemChunks.AddRange(chunks);
        }

        var chunksToWrite = expenseChunks
            .Concat(receiptChunks)
            .Concat(lineItemChunks)
            .ToList();

        // Atomic replace: delete old chunks + insert new ones in a single transaction.
        await _vectorStore.ReplaceDocumentChunksAsync(document.Id, chunksToWrite, cancellationToken);

        // Invalidate per-tenant chat response cache so future queries see updated chunks.
        if (_cacheService is not null)
        {
            await _cacheService.RemoveByPrefixAsync(
                ChatResponseCacheKey.TenantInvalidationKey(document.IdTenant),
                cancellationToken);
        }

        return chunksToWrite.Count;
    }

    public async Task<int> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException("Document id is required.", nameof(documentId));

        await _vectorStore.DeleteByDocumentIdAsync(documentId, cancellationToken);

        // Note: tenant id is not available here; the chunks delete handles tenant scope
        // via document_id FK. Caller should also invalidate cache if it has tenant context.
        return 0; // Actual count not available from delete; return 0 as sentinel.
    }

    internal static string BuildExpenseContent(ReviewedDocument document)
    {
        var vendorName = DocumentTextNormalizer.NormalizeVendorName(document.VendorName);
        var reference = DocumentTextNormalizer.NormalizeReference(document.Reference);
        var category = DocumentTextNormalizer.NormalizeCategory(document.Category);
        var builder = new StringBuilder();
        builder.AppendLine("Expense record");
        builder.AppendLine($"Merchant: {NormalizeForEvidence(vendorName)}");
        builder.AppendLine($"Merchant search key: {DocumentTextNormalizer.BuildSearchKey(vendorName)}");
        builder.AppendLine($"Reference: {NormalizeForEvidence(reference)}");
        builder.AppendLine($"Reference search key: {DocumentTextNormalizer.BuildSearchKey(reference)}");
        builder.AppendLine($"Expense date: {document.DocumentDate:yyyy-MM-dd}");
        builder.AppendLine($"Category: {NormalizeForEvidence(category)}");
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
                    $"- {NormalizeForEvidence(DocumentTextNormalizer.NormalizeLineItemName(lineItem.ItemName))}: quantity {lineItem.Quantity:0.##}, unit price {lineItem.UnitPrice:0.##}, total {lineItem.Total:0.##}");
            }
        }

        return builder.ToString().Trim();
    }

    internal static string BuildReceiptContent(ReviewedDocument document)
    {
        var vendorName = DocumentTextNormalizer.NormalizeVendorName(document.VendorName);
        var reference = DocumentTextNormalizer.NormalizeReference(document.Reference);
        var builder = new StringBuilder();
        builder.AppendLine("Receipt record");
        builder.AppendLine($"Original file name: {NormalizeForEvidence(document.OriginalFileName)}");
        builder.AppendLine($"Content type: {NormalizeForEvidence(document.ContentType)}");
        builder.AppendLine($"Merchant: {NormalizeForEvidence(vendorName)}");
        builder.AppendLine($"Merchant search key: {DocumentTextNormalizer.BuildSearchKey(vendorName)}");
        builder.AppendLine($"Reference: {NormalizeForEvidence(reference)}");
        builder.AppendLine($"Reference search key: {DocumentTextNormalizer.BuildSearchKey(reference)}");
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
                    $"- {NormalizeForEvidence(DocumentTextNormalizer.NormalizeLineItemName(lineItem.ItemName))}: quantity {lineItem.Quantity:0.##}, unit price {lineItem.UnitPrice:0.##}, total {lineItem.Total:0.##}");
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Per-line-item content (semantic chunking).
    /// Includes parent document context so the chunk is self-contained for retrieval.
    /// </summary>
    internal static string BuildLineItemContent(ReviewedDocument document, ReviewedDocumentLineItem lineItem)
    {
        var vendorName = DocumentTextNormalizer.NormalizeVendorName(document.VendorName);
        var reference = DocumentTextNormalizer.NormalizeReference(document.Reference);
        var builder = new StringBuilder();
        builder.AppendLine("Line item");
        builder.AppendLine($"Vendor: {NormalizeForEvidence(vendorName)}");
        builder.AppendLine($"Vendor search key: {DocumentTextNormalizer.BuildSearchKey(vendorName)}");
        builder.AppendLine($"Document reference: {NormalizeForEvidence(reference)}");
        builder.AppendLine($"Document reference search key: {DocumentTextNormalizer.BuildSearchKey(reference)}");
        builder.AppendLine($"Document date: {document.DocumentDate:yyyy-MM-dd}");
        builder.AppendLine($"Category: {NormalizeForEvidence(DocumentTextNormalizer.NormalizeCategory(document.Category))}");
        builder.AppendLine($"Item name: {NormalizeForEvidence(DocumentTextNormalizer.NormalizeLineItemName(lineItem.ItemName))}");
        builder.AppendLine($"Quantity: {lineItem.Quantity:0.##}");
        builder.AppendLine($"Unit price: {lineItem.UnitPrice:0.##}");
        if (lineItem.DiscountAmount > 0)
        {
            builder.Append($"Discount: {lineItem.DiscountAmount:0.##}");
            if (lineItem.DiscountPercent.HasValue)
                builder.Append($" ({lineItem.DiscountPercent.Value:0.##}%)");
            builder.AppendLine();
        }
        builder.AppendLine($"Total: {lineItem.Total:0.##}");
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
            if (chunk.Embedding == null || chunk.Embedding.Length != ExpectedEmbeddingDimensions)
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch for chunk {chunk.Id}. Expected {ExpectedEmbeddingDimensions} but got {(chunk.Embedding?.Length ?? 0)}.");
        }
    }

    private static string NormalizeForEvidence(string value)
    {
        var normalized = DocumentTextNormalizer.NormalizeEvidenceValue(value);

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
