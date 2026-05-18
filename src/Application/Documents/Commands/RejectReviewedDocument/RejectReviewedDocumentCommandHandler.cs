using FinFlow.Application.Budgets.Services;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using MediatR;

namespace FinFlow.Application.Documents.Commands.RejectReviewedDocument;

public sealed class RejectReviewedDocumentCommandHandler
    : IRequestHandler<RejectReviewedDocumentCommand, Result<ReviewedDocumentResponse>>
{
    private readonly IReviewedDocumentRepository _reviewedDocumentRepository;
    private readonly IBudgetReservationService _budgetReservation;
    private readonly IUnitOfWork _unitOfWork;

    public RejectReviewedDocumentCommandHandler(
        IReviewedDocumentRepository reviewedDocumentRepository,
        IBudgetReservationService budgetReservation,
        IUnitOfWork unitOfWork)
    {
        _reviewedDocumentRepository = reviewedDocumentRepository;
        _budgetReservation = budgetReservation;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReviewedDocumentResponse>> Handle(RejectReviewedDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await _reviewedDocumentRepository.GetByIdForUpdateAsync(request.DocumentId, request.TenantId, cancellationToken);
        if (document == null)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.NotFound);

        if (document.MembershipId == request.MembershipId)
            return Result.Failure<ReviewedDocumentResponse>(ReviewedDocumentErrors.SelfApprovalNotAllowed);

        // Snapshot whether we'd previously committed budget for this doc, so
        // we know whether to release. Only docs that were Approved (not Draft
        // or already Rejected) had a commitment.
        var wasApproved = document.Status == ReviewedDocumentStatus.Approved;
        var amount = document.TotalAmountInBaseCurrency;
        var month = document.DocumentDate.Month;
        var year = document.DocumentDate.Year;

        var rejectResult = document.Reject(request.Reason, request.MembershipId);
        if (rejectResult.IsFailure)
            return Result.Failure<ReviewedDocumentResponse>(rejectResult.Error);

        if (wasApproved)
        {
            var releaseResult = await _budgetReservation.ReleaseCommitmentAsync(
                new BudgetMovement(
                    TenantId: request.TenantId,
                    DepartmentId: document.IdDepartment,
                    Month: month,
                    Year: year,
                    AmountInBaseCurrency: amount,
                    SourceEntityId: document.Id,
                    SourceEntityType: "ReviewedDocument",
                    Reason: "Rejected after approval"),
                cancellationToken);
            if (releaseResult.IsFailure)
                return Result.Failure<ReviewedDocumentResponse>(releaseResult.Error);
        }

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
}
