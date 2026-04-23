using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Membership.Commands.RemoveMember;

public sealed record RemoveMemberCommand(
    Guid MembershipId,
    Guid TenantId,
    Guid ActorMembershipId,
    string? Reason) : ICommand<Result>;
