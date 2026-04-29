using FinFlow.Application.Common;
using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Vendors.Queries.GetVendorByTaxCode;

public sealed record GetVendorByTaxCodeQuery(
    Guid TenantId,
    string TaxCode
) : IQuery<Result<VendorResponse?>>;