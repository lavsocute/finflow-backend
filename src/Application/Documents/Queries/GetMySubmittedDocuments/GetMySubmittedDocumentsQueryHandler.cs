using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetMySubmittedDocuments;

public sealed class GetMySubmittedDocumentsQueryHandler
    : IRequestHandler<GetMySubmittedDocumentsQuery, Result<IReadOnlyList<MySubmittedDocumentSummaryResponse>>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;

    public GetMySubmittedDocumentsQueryHandler(IReviewedDocumentRepository reviewedDocumentRepository)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
    }

    public async Task<Result<IReadOnlyList<MySubmittedDocumentSummaryResponse>>> Handle(GetMySubmittedDocumentsQuery request, CancellationToken cancellationToken)
    {
        var documents = await _reviewedDocumentRepository.GetOwnedSubmittedAsync(request.TenantId, request.MembershipId, cancellationToken);
        var items = documents
            .Select(x => new MySubmittedDocumentSummaryResponse(
                x.Id,
                x.OriginalFileName,
                x.VendorName,
                x.Reference,
                x.TotalAmount,
                x.Category,
                x.Source,
                ToPersonalHistoryStatus(x),
                x.ReviewedByStaff,
                x.SubmittedAt,
                x.UpdatedAt,
                x.RejectionReason))
            .ToList();

        return Result.Success<IReadOnlyList<MySubmittedDocumentSummaryResponse>>(items);
    }

    private static string ToPersonalHistoryStatus(ReviewedDocument document) =>
        document.Status switch
        {
            ReviewedDocumentStatus.ReadyForApproval => "Submitted",
            ReviewedDocumentStatus.Approved => "Approved",
            ReviewedDocumentStatus.Rejected => "Rejected",
            _ => document.Status.ToString()
        };
}
