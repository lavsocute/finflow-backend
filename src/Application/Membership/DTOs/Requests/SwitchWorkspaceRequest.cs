namespace FinFlow.Application.Membership.DTOs.Requests;

public record SwitchWorkspaceRequest(
    Guid AccountId,
    Guid MembershipId,
    string CurrentRefreshToken);
