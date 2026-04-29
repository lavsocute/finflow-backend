using FinFlow.Application.Common;
using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Vendors.Commands.VerifyVendor;

public sealed record VerifyVendorCommand(
    Guid VendorId,
    Guid TenantId,
    Guid VerifiedByMembershipId
) : ICommand<Result<VendorResponse>>;