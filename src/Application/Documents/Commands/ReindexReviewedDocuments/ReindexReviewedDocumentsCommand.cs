using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Documents.Commands.ReindexReviewedDocuments;

public sealed record ReindexReviewedDocumentsCommand(
    Guid TenantId,
    Guid? DocumentId) : IRequest<Result<ReindexReviewedDocumentsResult>>;

public sealed record ReindexReviewedDocumentsResult(
    int ScannedDocuments,
    int IndexedDocuments,
    int FailedDocuments,
    int TotalChunks);
