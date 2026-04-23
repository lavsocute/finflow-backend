using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Platform.Commands.PlatformTransferOwnership;

public sealed record PlatformTransferOwnershipCommand(
    Guid MembershipId,
    Guid TenantId) : ICommand<Result>;
