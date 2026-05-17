using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Payments.Commands.UpdatePayment;

public sealed record UpdatePaymentCommand(
    Guid PaymentId,
    string PaymentMethod,
    string? Notes) : IRequest<Result<PaymentResponse>>;
