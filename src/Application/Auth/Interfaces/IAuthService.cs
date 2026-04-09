using FinFlow.Application.Auth.Dtos;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Auth.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, string? clientIp, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> CreateSharedTenantAsync(CreateSharedTenantRequest request, CancellationToken cancellationToken = default);
    Task<Result<TenantApprovalResponse>> CreateIsolatedTenantAsync(CreateIsolatedTenantRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PendingTenantApprovalResponse>>> GetPendingTenantRequestsAsync(CancellationToken cancellationToken = default);
    Task<Result<TenantApprovalDecisionResponse>> ApproveTenantAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<Result<TenantApprovalDecisionResponse>> RejectTenantAsync(Guid requestId, string reason, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> SwitchWorkspaceAsync(SwitchWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<Result<InvitationResponse>> InviteMemberAsync(InviteMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
