using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RefundPayment;

public sealed record RefundPaymentCommand(
    Guid PaymentId,
    decimal Amount,
    string Reason) : IRequest<Result<PaymentRefundResponse>>;
