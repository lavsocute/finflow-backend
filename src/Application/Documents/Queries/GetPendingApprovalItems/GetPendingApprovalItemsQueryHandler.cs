using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetPendingApprovalItems;

public sealed class GetPendingApprovalItemsQueryHandler
    : IRequestHandler<GetPendingApprovalItemsQuery, Result<IReadOnlyList<PendingApprovalItemResponse>>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;

    public GetPendingApprovalItemsQueryHandler(IReviewedDocumentRepository reviewedDocumentRepository)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
    }

    public async Task<Result<IReadOnlyList<PendingApprovalItemResponse>>> Handle(GetPendingApprovalItemsQuery request, CancellationToken cancellationToken)
    {
        var documents = await _reviewedDocumentRepository.GetReadyForApprovalAsync(request.TenantId, cancellationToken);
        var items = documents
            .OrderByDescending(x => x.SubmittedAt)
            .Select(x => new PendingApprovalItemResponse(
                x.Id,
                $"{x.VendorName} · {x.Reference}",
                x.ReviewedByStaff,
                $"{x.Category} · {x.Source}",
                x.TotalAmount,
                x.DueDate,
                x.TotalAmount >= 5000m ? "High" : "Medium",
                x.Status.ToString()))
            .ToList();

        return Result.Success<IReadOnlyList<PendingApprovalItemResponse>>(items);
    }
}
