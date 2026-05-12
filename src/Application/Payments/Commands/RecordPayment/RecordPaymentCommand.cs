using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RecordPayment;

public sealed record RecordPaymentCommand(
    Guid DocumentId,
    string PaymentMethod,
    string? Notes) : IRequest<Result<PaymentResponse>>;
