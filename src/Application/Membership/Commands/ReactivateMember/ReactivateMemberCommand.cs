using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Membership.Commands.ReactivateMember;

public sealed record ReactivateMemberCommand(
    Guid MembershipId,
    Guid TenantId,
    Guid ActorMembershipId) : ICommand<Result>;
