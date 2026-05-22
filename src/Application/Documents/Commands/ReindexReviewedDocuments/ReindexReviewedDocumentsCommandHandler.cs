using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.ReindexReviewedDocuments;

public sealed class ReindexReviewedDocumentsCommandHandler
    : IRequestHandler<ReindexReviewedDocumentsCommand, Result<ReindexReviewedDocumentsResult>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly IReviewedDocumentChunkIndexer _documentChunkIndexer;
    private readonly IVendorRepository? _vendorRepository;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly ILogger<ReindexReviewedDocumentsCommandHandler> _logger;

    public ReindexReviewedDocumentsCommandHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        IReviewedDocumentChunkIndexer documentChunkIndexer,
        ILogger<ReindexReviewedDocumentsCommandHandler> logger,
        IVendorRepository? vendorRepository = null,
        IUnitOfWork? unitOfWork = null)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _documentChunkIndexer = documentChunkIndexer;
        _logger = logger;
        _vendorRepository = vendorRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReindexReviewedDocumentsResult>> Handle(ReindexReviewedDocumentsCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReviewedDocument> documents;

        if (request.DocumentId.HasValue)
        {
            var document = await _reviewedDocumentRepository.GetByIdForUpdateAsync(
                request.DocumentId.Value,
                request.TenantId,
                cancellationToken);

            if (document is null)
                return Result.Failure<ReindexReviewedDocumentsResult>(ReviewedDocumentErrors.NotFound);

            documents = [document];
        }
        else
        {
            documents = await _reviewedDocumentRepository.GetAllActiveByTenantAsync(request.TenantId, cancellationToken);
        }

        var indexedDocuments = 0;
        var failedDocuments = 0;
        var totalChunks = 0;

        foreach (var document in documents)
        {
            try
            {
                await CanonicalizeVendorSnapshotAsync(document, cancellationToken);
                totalChunks += await _documentChunkIndexer.ReindexAsync(document, cancellationToken);
                indexedDocuments++;
            }
            catch (Exception ex)
            {
                failedDocuments++;
                _logger.LogWarning(
                    ex,
                    "Reviewed document reindex failed for tenant {TenantId} document {DocumentId}",
                    document.IdTenant,
                    document.Id);
            }
        }

        return Result.Success(new ReindexReviewedDocumentsResult(
            documents.Count,
            indexedDocuments,
            failedDocuments,
            totalChunks));
    }

    private async Task CanonicalizeVendorSnapshotAsync(ReviewedDocument document, CancellationToken cancellationToken)
    {
        if (!document.IdVendor.HasValue || _vendorRepository is null || _unitOfWork is null)
            return;

        var canonicalVendor = await _vendorRepository.GetByIdAsync(document.IdVendor.Value, document.IdTenant, cancellationToken);
        if (canonicalVendor is null)
            return;

        var currentName = document.VendorName?.Trim() ?? string.Empty;
        var currentTaxId = document.VendorTaxId?.Trim() ?? string.Empty;
        if (string.Equals(currentName, canonicalVendor.Name, StringComparison.Ordinal) &&
            string.Equals(currentTaxId, canonicalVendor.TaxCode, StringComparison.Ordinal))
            return;

        document.RefreshVendorSnapshot(canonicalVendor.Name, canonicalVendor.TaxCode);
        _reviewedDocumentRepository.Update(document);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
