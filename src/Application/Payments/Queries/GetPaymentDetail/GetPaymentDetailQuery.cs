using FinFlow.Application.Common;
using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Payments.Queries.GetPaymentDetail;

public sealed record GetPaymentDetailQuery(
    Guid TenantId,
    Guid? PaymentId = null,
    Guid? DocumentId = null) : IQuery<Result<PaymentDetailResponse?>>;
