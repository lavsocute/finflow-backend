using FinFlow.Application.Auth.Queries.GetCurrentWorkspace;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;

namespace FinFlow.UnitTests.Application.Auth;

public sealed class GetCurrentWorkspaceQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsWorkspace_WhenExplicitTenantAndMembershipAreProvided()
    {
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        var handler = new GetCurrentWorkspaceQueryHandler(
            new StubAccountRepository(new AccountLoginInfo(
                accountId,
                "claims.only@finflow.test",
                "hash",
                true,
                true,
                DateTime.UtcNow)),
            new StubTenantMembershipRepository(
                byId: new TenantMembershipSummary(
                    membershipId,
                    accountId,
                    tenantId,
                    null,
                    RoleType.Accountant,
                    false,
                    true,
                    DateTime.UtcNow)),
            new StubTenantRepository(new TenantSummary(
                tenantId,
                "Claims Only Workspace",
                "claims-only-workspace",
                TenancyModel.Shared,
                true)));

        var result = await handler.Handle(
            new GetCurrentWorkspaceQuery(accountId, tenantId, membershipId),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(accountId, result.Value.AccountId);
        Assert.Equal("claims.only@finflow.test", result.Value.Email);
        Assert.Equal(membershipId, result.Value.MembershipId);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal("claims-only-workspace", result.Value.TenantCode);
        Assert.Equal("Claims Only Workspace", result.Value.TenantName);
        Assert.Equal(RoleType.Accountant, result.Value.Role);
    }

    private sealed class StubAccountRepository : IAccountRepository
    {
        private readonly AccountLoginInfo? _loginInfo;

        public StubAccountRepository(AccountLoginInfo? loginInfo) => _loginInfo = loginInfo;

        public Task<AccountSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<AccountSummary?>(_loginInfo is null
                ? null
                : new AccountSummary(_loginInfo.Id, _loginInfo.Email, _loginInfo.IsActive, _loginInfo.IsEmailVerified, _loginInfo.EmailVerifiedAt));

        public Task<AccountLoginInfo?> GetLoginInfoByEmailAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult<AccountLoginInfo?>(_loginInfo);

        public Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<AccountLoginInfo?>(_loginInfo);

        public Task<bool> ExistsByEmailIgnoringTenantAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult(_loginInfo is not null && string.Equals(_loginInfo.Email, email, StringComparison.OrdinalIgnoreCase));

        public Task<FinFlow.Domain.Entities.Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<FinFlow.Domain.Entities.Account?>(null);

        public void Add(FinFlow.Domain.Entities.Account account) => throw new NotSupportedException();
        public void Update(FinFlow.Domain.Entities.Account account) => throw new NotSupportedException();
        public void Remove(FinFlow.Domain.Entities.Account account) => throw new NotSupportedException();
    }

    private sealed class StubTenantMembershipRepository : ITenantMembershipRepository
    {
        private readonly TenantMembershipSummary? _byId;

        public StubTenantMembershipRepository(TenantMembershipSummary? byId) => _byId = byId;

        public Task<TenantMembershipSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantMembershipSummary?>(_byId);

        public Task<TenantMembershipSummary?> GetActiveByAccountAndTenantAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantMembershipSummary?>(_byId is not null && _byId.AccountId == accountId && _byId.IdTenant == idTenant && _byId.IsActive
                ? _byId
                : null);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetActiveByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantMembershipSummary>>(_byId is not null && _byId.AccountId == accountId && _byId.IsActive
                ? [_byId]
                : []);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantMembershipSummary>>(_byId is not null && _byId.AccountId == accountId ? [_byId] : []);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantMembershipSummary>>(_byId is not null && _byId.IdTenant == idTenant ? [_byId] : []);

        public Task<bool> ExistsAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult(_byId is not null && _byId.AccountId == accountId && _byId.IdTenant == idTenant);

        public Task<bool> ExistsOwnerByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(_byId is not null && _byId.AccountId == accountId && _byId.IsOwner);

        public Task<FinFlow.Domain.Entities.TenantMembership?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<FinFlow.Domain.Entities.TenantMembership?>(null);

        public void Add(FinFlow.Domain.Entities.TenantMembership membership) => throw new NotSupportedException();
        public void Update(FinFlow.Domain.Entities.TenantMembership membership) => throw new NotSupportedException();
    }

    private sealed class StubTenantRepository : ITenantRepository
    {
        private readonly TenantSummary? _tenant;

        public StubTenantRepository(TenantSummary? tenant) => _tenant = tenant;

        public Task<TenantSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantSummary?>(_tenant);

        public Task<TenantSummary?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantSummary?>(_tenant is not null && _tenant.TenantCode == tenantCode ? _tenant : null);

        public Task<bool> ExistsByCodeAsync(string tenantCode, CancellationToken cancellationToken = default)
            => Task.FromResult(_tenant is not null && _tenant.TenantCode == tenantCode);

        public Task<IReadOnlyList<TenantSummary>> GetAllActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantSummary>>(_tenant is not null && _tenant.IsActive ? [_tenant] : []);

        public void Add(FinFlow.Domain.Entities.Tenant tenant) => throw new NotSupportedException();
        public void Update(FinFlow.Domain.Entities.Tenant tenant) => throw new NotSupportedException();
        public void Remove(FinFlow.Domain.Entities.Tenant tenant) => throw new NotSupportedException();
    }
}
