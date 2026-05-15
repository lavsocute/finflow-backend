namespace FinFlow.Domain.Interfaces;

/// <summary>
/// Represents the current request's tenant context. Read-only surface for the
/// Application and Domain layers — the values are set by infrastructure
/// (TenantMiddleware) at request entry.
///
/// For onboarding flows that need to act as a different tenant (e.g., AcceptInvite,
/// CreateSharedTenant, InviteMember), use <see cref="BeginScope"/> to push a temporary
/// context. The override automatically unwinds when the returned scope is disposed,
/// even if an exception is thrown. The override flows correctly across async/await
/// because it is backed by AsyncLocal.
/// </summary>
public interface ICurrentTenant
{
    Guid? Id { get; }
    Guid? MembershipId { get; }
    bool IsAvailable { get; }
    bool IsSuperAdmin { get; }

    /// <summary>
    /// Pushes a tenant context override that is only visible to the current async flow.
    /// Dispose the returned scope to unwind. Nesting is supported (LIFO).
    /// </summary>
    /// <param name="tenantId">Tenant to act as. null = no tenant context.</param>
    /// <param name="membershipId">Membership to act as.</param>
    /// <param name="isSuperAdmin">Whether the override grants super-admin privileges.</param>
    IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false);
}

/// <summary>
/// Setter surface — only TenantMiddleware (request entry) should depend on this.
/// Application/Domain code MUST NOT inject ICurrentTenantWriter.
/// </summary>
public interface ICurrentTenantWriter
{
    void SetFromRequest(Guid? tenantId, Guid? membershipId, bool isSuperAdmin);
}
