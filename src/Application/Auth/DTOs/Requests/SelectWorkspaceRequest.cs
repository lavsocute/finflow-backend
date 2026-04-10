namespace FinFlow.Application.Auth.DTOs.Requests;

public sealed record SelectWorkspaceRequest(
    Guid AccountId,
    Guid MembershipId);
