using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetMySubmittedDocument;

public sealed class GetMySubmittedDocumentQueryHandler
    : IRequestHandler<GetMySubmittedDocumentQuery, Result<MySubmittedDocumentDetailResponse>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;

    public GetMySubmittedDocumentQueryHandler(IReviewedDocumentRepository reviewedDocumentRepository)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
    }

    public async Task<Result<MySubmittedDocumentDetailResponse>> Handle(GetMySubmittedDocumentQuery request, CancellationToken cancellationToken)
    {
        var document = await _reviewedDocumentRepository.GetOwnedByIdAsync(
            request.DocumentId,
            request.TenantId,
            request.MembershipId,
            cancellationToken);

        if (document == null)
            return Result.Failure<MySubmittedDocumentDetailResponse>(ReviewedDocumentErrors.NotFound);

        return Result.Success(new MySubmittedDocumentDetailResponse(
            document.Id,
            document.OriginalFileName,
            document.ContentType,
            document.VendorName,
            document.Reference,
            document.DocumentDate,
            document.DueDate,
            document.Category,
            document.VendorTaxId ?? string.Empty,
            document.Subtotal,
            document.Vat,
            document.TotalAmount,
            document.Source,
            ToStatusString(document),
            document.ReviewedByStaff,
            document.SubmittedAt,
            document.UpdatedAt,
            document.RejectionReason,
            document.LineItems
                .Select(item => new MySubmittedDocumentDetailLineItemResponse(
                    item.ItemName,
                    item.Quantity,
                    item.UnitPrice,
                    item.Total))
                .ToList()));
    }

    private static string ToStatusString(ReviewedDocument document) =>
        document.Status switch
        {
            ReviewedDocumentStatus.ReadyForApproval => "Submitted",
            ReviewedDocumentStatus.Approved => "Approved",
            ReviewedDocumentStatus.Rejected => "Rejected",
            _ => document.Status.ToString()
        };
}