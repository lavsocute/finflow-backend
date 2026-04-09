namespace FinFlow.Application.Membership.DTOs.Requests;

public record AcceptInviteRequest(
    string InviteToken,
    string Password,
    string? ClientIp = null);
