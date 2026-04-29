using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;

namespace FinFlow.Application.Documents.Commands.SaveReviewedOcrDraft;

public sealed class SaveReviewedOcrDraftCommandHandler
    : IRequestHandler<SaveReviewedOcrDraftCommand, Result<Guid>>
{
    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SaveReviewedOcrDraftCommandHandler(
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork)
    {
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(SaveReviewedOcrDraftCommand request, CancellationToken cancellationToken)
    {
        var draft = await _uploadedDocumentDraftRepository.GetByIdAsync(
            request.DraftId,
            request.TenantId,
            request.MembershipId,
            cancellationToken);
        if (draft == null)
            return Result.Failure<Guid>(UploadedDocumentDraftErrors.NotFound);

        var lineItemsResult = request.LineItems
            .Select(item => UploadedDocumentDraftLineItem.Create(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
            .ToList();

        var firstFailure = lineItemsResult.FirstOrDefault(r => r.IsFailure);
        if (firstFailure is not null)
            return Result.Failure<Guid>(firstFailure.Error);

        var lineItems = lineItemsResult.Select(r => r.Value).ToList();

        var updateResult = draft.UpdateReviewedData(
            request.VendorName,
            request.Reference,
            request.DocumentDate,
            request.DueDate,
            request.Category,
            request.VendorTaxId,
            request.Subtotal,
            request.Vat,
            request.TotalAmount,
            request.ConfidenceLabel,
            lineItems);

        if (updateResult.IsFailure)
            return Result.Failure<Guid>(updateResult.Error);

        _uploadedDocumentDraftRepository.Update(draft);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(draft.Id);
    }
}