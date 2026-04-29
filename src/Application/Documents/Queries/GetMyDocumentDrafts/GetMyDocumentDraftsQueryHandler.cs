using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetMyDocumentDrafts;

public sealed class GetMyDocumentDraftsQueryHandler
    : IRequestHandler<GetMyDocumentDraftsQuery, Result<IReadOnlyList<MyDocumentDraftSummaryResponse>>>
{
    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;

    public GetMyDocumentDraftsQueryHandler(IUploadedDocumentDraftRepository uploadedDocumentDraftRepository)
    {
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
    }

    public async Task<Result<IReadOnlyList<MyDocumentDraftSummaryResponse>>> Handle(GetMyDocumentDraftsQuery request, CancellationToken cancellationToken)
    {
        var drafts = await _uploadedDocumentDraftRepository.GetOwnedActiveAsync(request.TenantId, request.MembershipId, cancellationToken);
        var items = drafts
            .Select(x => new MyDocumentDraftSummaryResponse(
                x.Id,
                x.OriginalFileName,
                x.VendorName,
                x.Reference,
                x.TotalAmount,
                x.Category,
                x.Source,
                x.ConfidenceLabel,
                x.UploadedByStaff,
                x.UploadedAt))
            .ToList();

        return Result.Success<IReadOnlyList<MyDocumentDraftSummaryResponse>>(items);
    }
}
