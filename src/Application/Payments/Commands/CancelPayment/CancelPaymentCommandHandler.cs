using FinFlow.Application.Budgets.Services;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.CancelPayment;

internal sealed class CancelPaymentCommandHandler : IRequestHandler<CancelPaymentCommand, Result<PaymentResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly IBudgetReservationService _budgetReservation;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public CancelPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IReviewedDocumentRepository documentRepository,
        IBudgetReservationService budgetReservation,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _documentRepository = documentRepository;
        _budgetReservation = budgetReservation;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentResponse>> Handle(CancelPaymentCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<PaymentResponse>(new Error("Payment.MembershipContext", "Membership context is not available."));

        var payment = await _paymentRepository.GetEntityByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<PaymentResponse>(PaymentErrors.NotFound);

        var cancelResult = payment.Cancel(request.Reason, _currentTenant.MembershipId.Value);
        if (cancelResult.IsFailure)
            return Result.Failure<PaymentResponse>(cancelResult.Error);

        // Budget bucket is keyed by document date (the original approve event
        // committed against documentDate.Month/Year — release must match).
        var (month, year) = await ResolveBudgetBucketAsync(payment, cancellationToken);

        var releaseResult = await _budgetReservation.ReleaseCommitmentAsync(
            new BudgetMovement(
                TenantId: payment.IdTenant,
                DepartmentId: payment.IdDepartment,
                Month: month,
                Year: year,
                AmountInBaseCurrency: payment.AmountInBaseCurrency,
                SourceEntityId: payment.Id,
                SourceEntityType: "Payment",
                Reason: $"Cancelled: {request.Reason}"),
            cancellationToken);
        if (releaseResult.IsFailure)
            return Result.Failure<PaymentResponse>(releaseResult.Error);

        _paymentRepository.Update(payment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PaymentResponse(
            payment.Id,
            payment.DocumentId,
            payment.Amount,
            payment.CurrencyCode,
            payment.AmountInBaseCurrency,
            payment.BaseCurrencyCode,
            payment.ExchangeRate,
            payment.Method.ToString(),
            payment.Status.ToString(),
            payment.RecordedAt,
            payment.RecordedByMembershipId,
            payment.Notes));
    }

    private async Task<(int Month, int Year)> ResolveBudgetBucketAsync(
        FinFlow.Domain.Expenses.Payment payment,
        CancellationToken cancellationToken)
    {
        var doc = await _documentRepository.GetByIdForUpdateAsync(payment.DocumentId, payment.IdTenant, cancellationToken);
        if (doc is not null)
            return (doc.DocumentDate.Month, doc.DocumentDate.Year);
        // Fallback when document isn't accessible (vd: deleted) — recorded date
        // is the next-best signal for budget attribution.
        return (payment.RecordedAt.Month, payment.RecordedAt.Year);
    }
}
