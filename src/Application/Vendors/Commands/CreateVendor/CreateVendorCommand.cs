using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Vendors.Commands.CreateVendor;

public sealed record CreateVendorCommand(
    Guid TenantId,
    string TaxCode,
    string Name
) : ICommand<Result<Guid>>;