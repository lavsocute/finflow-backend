namespace FinFlow.Application.Vendors.Services;

public sealed record VendorLinkedDocumentReadModel(
    Guid DocumentId,
    string Reference,
    string Category,
    string Status,
    decimal TotalAmount,
    string CurrencyCode,
    DateOnly DocumentDate);

public sealed record VendorDetailReadModel(
    Guid VendorId,
    int LinkedDocumentsCount,
    IReadOnlyList<VendorLinkedDocumentReadModel> RecentDocuments);

public interface IVendorWorkspaceReadService
{
    Task<IReadOnlyDictionary<Guid, int>> GetLinkedDocumentCountsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default);

    Task<VendorDetailReadModel?> GetDetailAsync(
        Guid tenantId,
        Guid vendorId,
        CancellationToken cancellationToken = default);
}
