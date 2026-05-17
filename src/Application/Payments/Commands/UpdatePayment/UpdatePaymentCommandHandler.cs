using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Payments.Commands.UpdatePayment;

internal sealed class UpdatePaymentCommandHandler : IRequestHandler<UpdatePaymentCommand, Result<PaymentResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePaymentCommandHandler(
        IPaymentRepository paymentRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentResponse>> Handle(UpdatePaymentCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<PaymentResponse>(new Error("Payment.MembershipContext", "Membership context is not available."));

        var payment = await _paymentRepository.GetEntityByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<PaymentResponse>(PaymentErrors.NotFound);

        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var method))
            return Result.Failure<PaymentResponse>(PaymentErrors.InvalidPaymentMethod);

        var updateResult = payment.Update(method, request.Notes, _currentTenant.MembershipId.Value);
        if (updateResult.IsFailure)
            return Result.Failure<PaymentResponse>(updateResult.Error);

        _paymentRepository.Update(payment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToResponse(payment));
    }

    private static PaymentResponse MapToResponse(Payment payment) => new(
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
        payment.Notes);
}
