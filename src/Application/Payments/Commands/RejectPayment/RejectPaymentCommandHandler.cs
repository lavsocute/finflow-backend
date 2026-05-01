using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RejectPayment;

internal sealed class RejectPaymentCommandHandler : IRequestHandler<RejectPaymentCommand, Result<PaymentResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public RejectPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
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

        var rejectResult = payment.Reject(_currentTenant.MembershipId.Value, request.Reason);
        if (rejectResult.IsFailure)
            return Result.Failure<PaymentResponse>(rejectResult.Error);

        _paymentRepository.Update(payment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PaymentResponse(
            payment.Id,
            payment.DocumentId,
            payment.Amount,
            payment.CurrencyCode.ToString(),
            payment.AmountInVnd,
            payment.Method.ToString(),
            payment.Status.ToString(),
            payment.RecordedAt,
            payment.RecordedByMembershipId,
            payment.Notes));
    }
}