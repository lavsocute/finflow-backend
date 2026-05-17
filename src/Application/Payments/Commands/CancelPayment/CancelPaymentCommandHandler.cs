using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.CancelPayment;

internal sealed class CancelPaymentCommandHandler : IRequestHandler<CancelPaymentCommand, Result<PaymentResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public CancelPaymentCommandHandler(
        IPaymentRepository paymentRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
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
