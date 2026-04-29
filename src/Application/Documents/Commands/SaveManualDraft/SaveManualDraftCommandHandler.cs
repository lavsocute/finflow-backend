using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;

namespace FinFlow.Application.Documents.Commands.SaveManualDraft;

public sealed class SaveManualDraftCommandHandler
    : IRequestHandler<SaveManualDraftCommand, Result<Guid>>
{
    private readonly IUploadedDocumentDraftRepository _uploadedDocumentDraftRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SaveManualDraftCommandHandler(
        IUploadedDocumentDraftRepository uploadedDocumentDraftRepository,
        IUnitOfWork unitOfWork)
    {
        _uploadedDocumentDraftRepository = uploadedDocumentDraftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(SaveManualDraftCommand request, CancellationToken cancellationToken)
    {
        var lineItemsResult = request.LineItems
            .Select(item => UploadedDocumentDraftLineItem.Create(item.ItemName, item.Quantity, item.UnitPrice, item.Total))
            .ToList();

        var firstFailure = lineItemsResult.FirstOrDefault(r => r.IsFailure);
        if (firstFailure is not null)
            return Result.Failure<Guid>(firstFailure.Error);

        var lineItems = lineItemsResult.Select(r => r.Value).ToList();

        var draftResult = UploadedDocumentDraft.CreateManual(
            request.TenantId,
            request.MembershipId,
            request.OriginalFileName,
            request.VendorName,
            request.Reference,
            request.DocumentDate,
            request.DueDate,
            request.Category,
            request.VendorTaxId,
            request.Subtotal,
            request.Vat,
            request.TotalAmount,
            request.ReviewedByStaff,
            lineItems);

        if (draftResult.IsFailure)
            return Result.Failure<Guid>(draftResult.Error);

        _uploadedDocumentDraftRepository.Add(draftResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(draftResult.Value.Id);
    }
}