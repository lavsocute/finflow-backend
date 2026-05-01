using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Payments.Commands.RecordPayment;

public sealed record RecordPaymentCommand(
    Guid DocumentId,
    decimal Amount,
    string CurrencyCode,
    string PaymentMethod,
    string? Notes,
    decimal? ExchangeRate) : IRequest<Result<PaymentResponse>>;