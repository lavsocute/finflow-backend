using FinFlow.Application.Budgets.Services;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RejectPayment;

internal sealed class RejectPaymentCommandHandler : IRequestHandler<RejectPaymentCommand, Result<PaymentResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly IBudgetReservationService _budgetReservation;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public RejectPaymentCommandHandler(
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

    public async Task<Result<PaymentResponse>> Handle(RejectPaymentCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.Id.HasValue)
            return Result.Failure<PaymentResponse>(new Error("Payment.TenantContext", "Tenant context is not available."));
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<PaymentResponse>(new Error("Payment.MembershipContext", "Membership context is not available."));

        var payment = await _paymentRepository.GetEntityByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<PaymentResponse>(PaymentErrors.NotFound);

        if (payment.Status != PaymentStatus.Pending)
            return Result.Failure<PaymentResponse>(PaymentErrors.AlreadyProcessed);

        var rejectResult = payment.Reject(_currentTenant.MembershipId.Value, request.Type, request.Reason);
        if (rejectResult.IsFailure)
            return Result.Failure<PaymentResponse>(rejectResult.Error);

        // Release the commitment that was reserved when the document was
        // approved. Use document date for the budget bucket so it matches
        // the original commit.
        var doc = await _documentRepository.GetByIdForUpdateAsync(payment.DocumentId, payment.IdTenant, cancellationToken);
        var month = doc?.DocumentDate.Month ?? payment.RecordedAt.Month;
        var year = doc?.DocumentDate.Year ?? payment.RecordedAt.Year;

        var releaseResult = await _budgetReservation.ReleaseCommitmentAsync(
            new BudgetMovement(
                TenantId: payment.IdTenant,
                DepartmentId: payment.IdDepartment,
                Month: month,
                Year: year,
                AmountInBaseCurrency: payment.AmountInBaseCurrency,
                SourceEntityId: payment.Id,
                SourceEntityType: "Payment",
                Reason: $"Rejected: {request.Type}"),
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
}
