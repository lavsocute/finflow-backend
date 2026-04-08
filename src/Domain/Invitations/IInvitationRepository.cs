using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Invitations;

public record InvitationSummary(
    Guid Id,
    string Email,
    Guid IdTenant,
    Guid InvitedByMembershipId,
    RoleType Role,
    DateTime ExpiresAt,
    DateTime? AcceptedAt,
    DateTime? RevokedAt,
    bool IsActive);

public interface IInvitationRepository
{
    Task<bool> HasPendingInvitationAsync(string email, Guid idTenant, CancellationToken cancellationToken = default);
    Task<InvitationSummary?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvitationSummary>> GetPendingByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default);
    Task<FinFlow.Domain.Entities.Invitation?> GetByTokenForUpdateAsync(string rawToken, CancellationToken cancellationToken = default);

    void Add(FinFlow.Domain.Entities.Invitation invitation);
    void Update(FinFlow.Domain.Entities.Invitation invitation);
}
