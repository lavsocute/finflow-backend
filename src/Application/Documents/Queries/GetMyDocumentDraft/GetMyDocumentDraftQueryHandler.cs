using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetMyDocumentDraft;

public sealed class GetMyDocumentDraftQueryHandler
    : IRequestHandler<GetMyDocumentDraftQuery, Result<DocumentOcrDraftResponse>>
{
    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;

    public GetMyDocumentDraftQueryHandler(IUploadedDocumentDraftRepository uploadedDocumentDraftRepository)
    {
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
    }

    public async Task<Result<DocumentOcrDraftResponse>> Handle(GetMyDocumentDraftQuery request, CancellationToken cancellationToken)
    {
        var draft = await _uploadedDocumentDraftRepository.GetByIdAsync(
            request.DocumentId,
            request.TenantId,
            request.MembershipId,
            cancellationToken);

        if (draft == null)
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.NotFound);

        return Result.Success(ToResponse(draft));
    }

    private static DocumentOcrDraftResponse ToResponse(UploadedDocumentDraft draft) =>
        new(
            draft.Id,
            draft.OriginalFileName,
            draft.ContentType,
            draft.VendorName,
            draft.Reference,
            draft.DocumentDate,
            draft.DueDate,
            draft.Category,
            draft.VendorTaxId ?? string.Empty,
            draft.Subtotal,
            draft.Vat,
            draft.TotalAmount,
            draft.Source,
            draft.UploadedByStaff,
            draft.ConfidenceLabel,
            draft.HasImage,
            draft.LineItems
                .Select(item => new DocumentOcrDraftLineItemResponse(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
                .ToList());
}
