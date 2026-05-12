using FinFlow.Application.Common;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Payments.Queries.GetPaymentQueue;

public sealed record GetPaymentQueueQuery(
    Guid TenantId,
    string? Status = null,
    string? Search = null) : IQuery<Result<IReadOnlyList<PaymentQueueItemResponse>>>;
