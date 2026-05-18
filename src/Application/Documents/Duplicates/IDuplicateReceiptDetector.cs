namespace FinFlow.Application.Documents.Duplicates;

/// <summary>
/// Looks up other documents in the tenant that share the given dedup hash
/// within a sliding window. Used by the document submit flow to flag
/// potential duplicates as a soft warning.
/// </summary>
public interface IDuplicateReceiptDetector
{
    Task<IReadOnlyList<Guid>> FindMatchesAsync(
        Guid tenantId,
        string dedupHash,
        Guid currentDocumentId,
        int slidingWindowDays,
        CancellationToken cancellationToken = default);
}
