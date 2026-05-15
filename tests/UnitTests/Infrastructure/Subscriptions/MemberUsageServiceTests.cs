using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.TenantUsageSnapshots;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Infrastructure.Subscriptions;

namespace FinFlow.UnitTests.Infrastructure.Subscriptions;

public sealed class MemberUsageServiceTests
{
    [Fact]
    public async Task GetCurrentUsageAsync_CreatesSnapshotWhenMissing()
    {
        var repository = new InMemoryMemberUsageSnapshotRepository();
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var membershipId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var membershipRepository = new StubTenantMembershipRepository(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                null,
                RoleType.Staff,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null));
        var service = new MemberUsageService(repository, membershipRepository);
        var periodStart = new DateOnly(2026, 5, 1);
        var periodEnd = new DateOnly(2026, 5, 31);

        var usage = await service.GetCurrentUsageAsync(tenantId, membershipId, periodStart, periodEnd, CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(tenantId, usage.IdTenant);
        Assert.Equal(membershipId, usage.MembershipId);
        Assert.Equal(periodStart, usage.PeriodStart);
        Assert.Equal(periodEnd, usage.PeriodEnd);
        Assert.Equal(0, usage.OcrPagesUsed);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task RecordChatbotUsageAsync_IncrementsExistingSnapshot()
    {
        var repository = new InMemoryMemberUsageSnapshotRepository();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var membershipRepository = new StubTenantMembershipRepository(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                null,
                RoleType.Staff,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null));
        var service = new MemberUsageService(repository, membershipRepository);
        var periodStart = new DateOnly(2026, 5, 1);
        var periodEnd = new DateOnly(2026, 5, 31);

        await service.RecordChatbotUsageAsync(tenantId, membershipId, 2, periodStart, periodEnd, CancellationToken.None);

        var usage = await service.GetCurrentUsageAsync(tenantId, membershipId, periodStart, periodEnd, CancellationToken.None);

        Assert.Equal(2, usage.ChatbotMessagesUsed);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task RecordChatbotUsageAsync_UsesCanonicalSnapshot_WhenConcurrentCreateWinsFirstWrite()
    {
        var repository = new InMemoryMemberUsageSnapshotRepository
        {
            SimulateConcurrentInsertOnCreate = true
        };
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var membershipRepository = new StubTenantMembershipRepository(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                null,
                RoleType.Staff,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null));
        var service = new MemberUsageService(repository, membershipRepository);
        var periodStart = new DateOnly(2026, 5, 1);
        var periodEnd = new DateOnly(2026, 5, 31);

        await service.RecordChatbotUsageAsync(tenantId, membershipId, 4, periodStart, periodEnd, CancellationToken.None);

        Assert.Equal(1, repository.GetOrCreateCalls);
        Assert.Single(repository.Items);
        Assert.Equal(4, repository.Items.Single().ChatbotMessagesUsed);
    }

    [Fact]
    public async Task GetCurrentUsageAsync_Throws_WhenMembershipDoesNotExist()
    {
        var repository = new InMemoryMemberUsageSnapshotRepository();
        var membershipRepository = new StubTenantMembershipRepository(null);
        var service = new MemberUsageService(repository, membershipRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCurrentUsageAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31),
                CancellationToken.None));

        Assert.Equal("Membership is required for this tenant.", exception.Message);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task GetCurrentUsageAsync_Throws_WhenMembershipBelongsToDifferentTenant()
    {
        var repository = new InMemoryMemberUsageSnapshotRepository();
        var requestedTenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var membershipRepository = new StubTenantMembershipRepository(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                RoleType.Staff,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null));
        var service = new MemberUsageService(repository, membershipRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCurrentUsageAsync(
                requestedTenantId,
                membershipId,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31),
                CancellationToken.None));

        Assert.Equal("Membership does not belong to the tenant.", exception.Message);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task GetCurrentUsageAsync_Throws_WhenMembershipIsInactive()
    {
        var repository = new InMemoryMemberUsageSnapshotRepository();
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var membershipRepository = new StubTenantMembershipRepository(
            new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                null,
                RoleType.Staff,
                false,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow,
                Guid.NewGuid(),
                "deactivated"));
        var service = new MemberUsageService(repository, membershipRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCurrentUsageAsync(
                tenantId,
                membershipId,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31),
                CancellationToken.None));

        Assert.Equal("Membership must be active for usage tracking.", exception.Message);
        Assert.Empty(repository.Items);
    }

    private sealed class InMemoryMemberUsageSnapshotRepository : IMemberUsageSnapshotRepository
    {
        private readonly List<MemberUsageSnapshot> _items = [];

        public IReadOnlyCollection<MemberUsageSnapshot> Items => _items;
        public bool SimulateConcurrentInsertOnCreate { get; init; }
        public int GetOrCreateCalls { get; private set; }

        public Task<MemberUsageSnapshot?> GetByMembershipAndPeriodAsync(
            Guid tenantId,
            Guid membershipId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default)
        {
            var snapshot = _items.FirstOrDefault(x =>
                x.IdTenant == tenantId &&
                x.MembershipId == membershipId &&
                x.PeriodStart == periodStart &&
                x.PeriodEnd == periodEnd);

            return Task.FromResult(snapshot);
        }

        public Task<MemberUsageSnapshot> GetOrCreateAsync(
            MemberUsageSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            GetOrCreateCalls++;

            var existingSnapshot = _items.FirstOrDefault(x =>
                x.IdTenant == snapshot.IdTenant &&
                x.MembershipId == snapshot.MembershipId &&
                x.PeriodStart == snapshot.PeriodStart &&
                x.PeriodEnd == snapshot.PeriodEnd);

            if (existingSnapshot is not null)
                return Task.FromResult(existingSnapshot);

            if (SimulateConcurrentInsertOnCreate)
            {
                var externalSnapshot = MemberUsageSnapshot.Create(
                    snapshot.IdTenant,
                    snapshot.MembershipId,
                    snapshot.PeriodStart,
                    snapshot.PeriodEnd).Value;
                _items.Add(externalSnapshot);
                return Task.FromResult(externalSnapshot);
            }

            _items.Add(snapshot);
            return Task.FromResult(snapshot);
        }

        public void Add(MemberUsageSnapshot snapshot) => _items.Add(snapshot);

        public void Update(MemberUsageSnapshot snapshot)
        {
            var index = _items.FindIndex(x => x.Id == snapshot.Id);
            if (index >= 0)
            {
                _items[index] = snapshot;
            }
        }
    }

    private sealed class StubTenantMembershipRepository : ITenantMembershipRepository
    {
        private readonly TenantMembershipSummary? _membership;

        public StubTenantMembershipRepository(TenantMembershipSummary? membership)
        {
            _membership = membership;
        }

        public Task<TenantMembershipSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_membership?.Id == id ? _membership : null);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantMembershipSummary>>(_membership is not null && ids.Contains(_membership.Id) ? [_membership] : []);

        public Task<TenantMembershipSummary?> GetActiveByAccountAndTenantAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantMembershipSummary?>(null);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetActiveByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantMembershipSummary>>([]);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantMembershipSummary>>([]);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantMembershipSummary>>([]);

        public Task<bool> ExistsAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> ExistsOwnerByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<TenantMembership?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantMembership?>(null);

        public void Add(TenantMembership membership) => throw new NotSupportedException();

        public void Update(TenantMembership membership) => throw new NotSupportedException();
    }
}
