using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Expenses;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.WithdrawReviewedDocument;

internal sealed class WithdrawReviewedDocumentCommandHandler
    : IRequestHandler<WithdrawReviewedDocumentCommand, Result<ReviewedDocumentResponse>>
{
    private readonly IReviewedDocumentRepository _docRepo;
    private readonly IUploadedDocumentDraftRepository _draftRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReviewedDocumentChunkIndexer _indexer;
    private readonly ILogger<WithdrawReviewedDocumentCommandHandler> _logger;

    public WithdrawReviewedDocumentCommandHandler(
        IReviewedDocumentRepository docRepo,
        IUploadedDocumentDraftRepository draftRepo,
        IPaymentRepository paymentRepo,
        IUnitOfWork unitOfWork,
        IReviewedDocumentChunkIndexer indexer,
        ILogger<WithdrawReviewedDocumentCommandHandler> logger)
    {
        _docRepo = docRepo;
        _draftRepo = draftRepo;
        _paymentRepo = paymentRepo;
        _unitOfWork = unitOfWork;
        _indexer = indexer;
        _logger = logger;
    }

    public async Task<Result<ReviewedDocumentResponse>> Handle(WithdrawReviewedDocumentCommand cmd, CancellationToken ct)
    {
        // 1. Load with FOR UPDATE lock (pessimistic — race with Approve)
        var doc = await _docRepo.GetByIdForUpdateAsync(cmd.DocumentId, cmd.TenantId, ct);
        if (doc is null)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.NotFound);

        // 2. Authorization
        if (!cmd.IsTenantOwner && doc.MembershipId != cmd.MembershipId)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.Unauthorized);

        // 3. Payment guard
        var hasPayment = await _paymentRepo.ExistsByDocumentIdAsync(cmd.DocumentId, ct);
        if (hasPayment)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.WithdrawnHasPayment);

        // 4. Domain transition
        var withdrawResult = doc.Withdraw();
        if (withdrawResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(withdrawResult.Error);

        _docRepo.Update(doc);

        // 5. Reactivate or recreate draft
        var draft = await _draftRepo.GetByIdAsync(doc.Id, doc.IdTenant, doc.MembershipId, includeInactive: true, ct);
        if (draft is not null)
        {
            var reactivate = draft.ReactivateFromSnapshot(doc);
            if (reactivate.IsFailure)
                return Result.Failure<ReviewedDocumentResponse>(reactivate.Error);
            _draftRepo.Update(draft);
        }
        else
        {
            // Manual submission case: create fresh draft from snapshot
            var newDraftResult = UploadedDocumentDraft.CreateFromSnapshot(doc, doc.ReviewedByStaff);
            if (newDraftResult.IsFailure)
                return Result.Failure<ReviewedDocumentResponse>(newDraftResult.Error);
            _draftRepo.Add(newDraftResult.Value);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // 6. Best-effort: remove vector chunks
        try
        {
            await _indexer.RemoveAsync(doc.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chunk removal failed after withdraw for doc {DocumentId}", doc.Id);
        }

        _logger.LogInformation(
            "Reviewed document withdrawn: {DocumentId} tenant={TenantId} membership={MembershipId} byTenantOwner={IsTenantOwner}",
            doc.Id, cmd.TenantId, cmd.MembershipId, cmd.IsTenantOwner);

        return Result.Success(new ReviewedDocumentResponse(
            doc.Id,
            doc.Status.ToString(),
            doc.SubmittedAt,
            doc.VendorName,
            doc.Reference,
            doc.TotalAmount,
            doc.ReviewedByStaff,
            doc.CurrencyCode,
            doc.ExchangeRate,
            doc.BaseCurrencyCode,
            doc.TotalAmountInBaseCurrency));
    }
}
