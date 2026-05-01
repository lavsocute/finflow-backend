using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RejectPayment;

public sealed record RejectPaymentCommand(Guid PaymentId, string Reason) : IRequest<Result<PaymentResponse>>;