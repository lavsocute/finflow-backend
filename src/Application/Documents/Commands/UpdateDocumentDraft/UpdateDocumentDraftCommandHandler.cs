using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.UpdateDocumentDraft;

internal sealed class UpdateDocumentDraftCommandHandler
    : IRequestHandler<UpdateDocumentDraftCommand, Result<DocumentOcrDraftResponse>>
{
    private readonly IUploadedDocumentDraftRepository _draftRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateDocumentDraftCommandHandler> _logger;

    public UpdateDocumentDraftCommandHandler(
        IUploadedDocumentDraftRepository draftRepo,
        IUnitOfWork unitOfWork,
        ILogger<UpdateDocumentDraftCommandHandler> logger)
    {
        _draftRepo = draftRepo;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DocumentOcrDraftResponse>> Handle(UpdateDocumentDraftCommand cmd, CancellationToken ct)
    {
        var draft = cmd.IsTenantOwner
            ? await _draftRepo.GetByTenantIdAsync(cmd.DraftId, cmd.TenantId, ct)
            : await _draftRepo.GetByIdAsync(cmd.DraftId, cmd.TenantId, cmd.MembershipId, ct);

        if (draft is null)
            return Result.Failure<DocumentOcrDraftResponse>(UploadedDocumentDraftErrors.NotFound);

        // Build line items via factory (validates discount invariants per line)
        var lineItems = new List<UploadedDocumentDraftLineItem>();
        foreach (var li in cmd.LineItems)
        {
            var r = UploadedDocumentDraftLineItem.Create(
                li.ItemName, li.Quantity, li.UnitPrice, li.DiscountPercent, li.DiscountAmount, li.Total);
            if (r.IsFailure)
                return Result.Failure<DocumentOcrDraftResponse>(r.Error);
            lineItems.Add(r.Value);
        }

        var updateResult = draft.UpdateDraftFields(
            cmd.VendorName,
            cmd.Reference,
            cmd.DocumentDate,
            cmd.Category,
            cmd.VendorTaxId,
            cmd.Subtotal,
            cmd.DocumentDiscountPercent,
            cmd.DocumentDiscountAmount,
            cmd.Vat,
            cmd.TotalAmount,
            cmd.ConfidenceLabel,
            lineItems);

        if (updateResult.IsFailure)
            return Result.Failure<DocumentOcrDraftResponse>(updateResult.Error);

        _draftRepo.Update(draft);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Document draft updated: {DraftId} tenant={TenantId} membership={MembershipId} byTenantOwner={IsTenantOwner}",
            draft.Id, cmd.TenantId, cmd.MembershipId, cmd.IsTenantOwner);

        return Result.Success(MapToResponse(draft));
    }

    private static DocumentOcrDraftResponse MapToResponse(UploadedDocumentDraft draft) =>
        new(
            draft.Id,
            draft.OriginalFileName,
            draft.ContentType,
            draft.VendorName,
            draft.Reference,
            draft.DocumentDate,
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
                .Select(li => new DocumentOcrDraftLineItemResponse(li.ItemName, li.Quantity, li.UnitPrice, li.Total))
                .ToList());
}
