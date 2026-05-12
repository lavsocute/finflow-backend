using FinFlow.Domain.Expenses;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RejectPayment;

public sealed record RejectPaymentCommand(Guid PaymentId, PaymentRejectType Type, string? Reason) : IRequest<Result<PaymentResponse>>;
