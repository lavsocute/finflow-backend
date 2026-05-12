using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Payments.Commands.ConfirmPayment;

public sealed record ConfirmPaymentCommand(Guid PaymentId, string? ExecutionReference) : IRequest<Result<PaymentResponse>>;
