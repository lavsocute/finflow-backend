using FinFlow.Domain.Enums;

namespace FinFlow.Application.Membership.DTOs.Requests;

public record InviteMemberRequest(
    Guid InviterAccountId,
    Guid InviterMembershipId,
    string Email,
    RoleType Role);
