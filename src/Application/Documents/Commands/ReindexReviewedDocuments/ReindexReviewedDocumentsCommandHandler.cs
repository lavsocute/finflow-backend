using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.ReindexReviewedDocuments;

public sealed class ReindexReviewedDocumentsCommandHandler
    : IRequestHandler<ReindexReviewedDocumentsCommand, Result<ReindexReviewedDocumentsResult>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly IReviewedDocumentChunkIndexer _documentChunkIndexer;
    private readonly ILogger<ReindexReviewedDocumentsCommandHandler> _logger;

    public ReindexReviewedDocumentsCommandHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        IReviewedDocumentChunkIndexer documentChunkIndexer,
        ILogger<ReindexReviewedDocumentsCommandHandler> logger)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _documentChunkIndexer = documentChunkIndexer;
        _logger = logger;
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
}
