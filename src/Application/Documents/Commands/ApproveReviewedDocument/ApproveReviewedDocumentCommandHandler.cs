using FinFlow.Application.Budgets.Services;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSettings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.ApproveReviewedDocument;

public sealed class ApproveReviewedDocumentCommandHandler
    : IRequestHandler<ApproveReviewedDocumentCommand, Result<ReviewedDocumentResponse>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly ITenantSettingsRepository _tenantSettingsRepository;
    private readonly IBudgetGuard _budgetGuard;
    private readonly IBudgetReservationService _budgetReservation;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReviewedDocumentChunkIndexer _documentChunkIndexer;
    private readonly ILogger<ApproveReviewedDocumentCommandHandler> _logger;

    public ApproveReviewedDocumentCommandHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        ITenantSettingsRepository tenantSettingsRepository,
        IBudgetGuard budgetGuard,
        IBudgetReservationService budgetReservation,
        IUnitOfWork unitOfWork,
        IReviewedDocumentChunkIndexer documentChunkIndexer,
        ILogger<ApproveReviewedDocumentCommandHandler> logger)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _tenantSettingsRepository = tenantSettingsRepository;
        _budgetGuard = budgetGuard;
        _budgetReservation = budgetReservation;
        _unitOfWork = unitOfWork;
        _documentChunkIndexer = documentChunkIndexer;
        _logger = logger;
    }

    public async Task<Result<ReviewedDocumentResponse>> Handle(ApproveReviewedDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await _reviewedDocumentRepository.GetByIdForUpdateAsync(request.DocumentId, request.TenantId, cancellationToken);
        if (document == null)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.NotFound);

        // Load tenant approval policy (graceful fallback to permissive defaults).
        var settings = await _tenantSettingsRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        var requireDifferentApprover = settings?.RequireDifferentApprover ?? true;
        var escalationThreshold = settings?.EscalationThreshold ?? decimal.MaxValue;
        var isEscalationEnabled = settings?.IsEscalationEnabled ?? false;

        if (requireDifferentApprover && document.MembershipId == request.MembershipId)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.SelfApprovalNotAllowed);

        var amount = document.TotalAmountInBaseCurrency;
        var month = document.DocumentDate.Month;
        var year = document.DocumentDate.Year;

        // ─── Escalation logic ───
        // If escalation is enabled and amount exceeds threshold, a Manager-level
        // approval only escalates (PendingEscalation). The document is NOT fully
        // approved until a higher-role user (Accountant/TenantAdmin) approves it.
        // If the document is already PendingEscalation, this call is the second-
        // level approval and proceeds to full Approved status.
        if (isEscalationEnabled
            && amount > escalationThreshold
            && document.Status == ReviewedDocumentStatus.ReadyForApproval
            && request.ApproverRole == RoleType.Manager)
        {
            var escalateResult = document.Escalate(request.MembershipId);
            if (escalateResult.IsFailure)
                return Result.Failure<ReviewedDocumentResponse>(escalateResult.Error);

            _reviewedDocumentRepository.Update(document);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new ReviewedDocumentResponse(
                document.Id,
                document.Status.ToString(),
                document.SubmittedAt,
                document.VendorName,
                document.Reference,
                document.TotalAmount,
                document.ReviewedByStaff,
                document.CurrencyCode,
                document.ExchangeRate,
                document.BaseCurrencyCode,
                document.TotalAmountInBaseCurrency));
        }

        // ─── Budget gate ───
        var check = await _budgetGuard.CheckAsync(
            request.TenantId, document.IdDepartment, month, year, amount, cancellationToken);

        if (check.IsBlocked)
            return Result.Failure<ReviewedDocumentResponse>(BudgetErrors.HardBlocked);

        var hasOverride = !string.IsNullOrWhiteSpace(request.OverrideJustification);
        if (check.RequiresOverride && !hasOverride)
            return Result.Failure<ReviewedDocumentResponse>(BudgetErrors.OverrideRequired);
        if (hasOverride && request.OverrideJustification!.Length > 1000)
            return Result.Failure<ReviewedDocumentResponse>(BudgetErrors.OverrideJustificationRequired);

        var approveResult = document.Approve(request.MembershipId);
        if (approveResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(approveResult.Error);

        // Reserve the budget.
        var movement = new BudgetMovement(
            TenantId: request.TenantId,
            DepartmentId: document.IdDepartment,
            Month: month,
            Year: year,
            AmountInBaseCurrency: amount,
            SourceEntityId: document.Id,
            SourceEntityType: "ReviewedDocument",
            Reason: hasOverride && check.RequiresOverride ? "Approved-with-override" : "Approved");

        Result commitResult;
        if (hasOverride && check.RequiresOverride)
        {
            var overAmount = Math.Max(0m, amount - check.AvailableBefore);
            commitResult = await _budgetReservation.CommitWithOverrideAsync(
                movement,
                BudgetExceededTrigger.ApproveDocument,
                request.MembershipId,
                request.OverrideJustification!,
                overAmount,
                cancellationToken);
        }
        else
        {
            commitResult = await _budgetReservation.CommitAsync(
                movement, BudgetExceededTrigger.ApproveDocument, cancellationToken);
        }
        if (commitResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(commitResult.Error);

        _reviewedDocumentRepository.Update(document);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _documentChunkIndexer.ReindexAsync(document, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Reviewed document auto-index failed after approval for tenant {TenantId} document {DocumentId}",
                document.IdTenant,
                document.Id);
        }

        return Result.Success(new ReviewedDocumentResponse(
            document.Id,
            document.Status.ToString(),
            document.SubmittedAt,
            document.VendorName,
            document.Reference,
            document.TotalAmount,
            document.ReviewedByStaff,
            document.CurrencyCode,
            document.ExchangeRate,
            document.BaseCurrencyCode,
            document.TotalAmountInBaseCurrency));
    }
}
