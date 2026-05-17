using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Payments.Commands.CancelPayment;

public sealed record CancelPaymentCommand(
    Guid PaymentId,
    string Reason) : IRequest<Result<PaymentResponse>>;
