using FinFlow.Application.Common;
using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Vendors.Queries.GetVendors;

public sealed record GetVendorsQuery(
    Guid TenantId,
    bool? IsVerified = null
) : IQuery<Result<IReadOnlyList<VendorResponse>>>;