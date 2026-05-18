using FinFlow.Application.Vendors.Services;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinFlow.UnitTests.Application.Vendors;

public class VendorLinkResolverTests
{
    [Fact]
    public async Task EmptyTaxId_ReturnsNotApplicable()
    {
        var sut = BuildResolver(out _);

        var result = await sut.ResolveAsync(BuildRequest(taxId: null));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.VendorId);
        Assert.False(result.Value.WasAutoCreated);
    }

    [Fact]
    public async Task WhitespaceTaxId_ReturnsNotApplicable()
    {
        var sut = BuildResolver(out _);

        var result = await sut.ResolveAsync(BuildRequest(taxId: "   "));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.VendorId);
    }

    [Fact]
    public async Task InvalidShape_TaxIdContainsLetters_ReturnsNotApplicable()
    {
        var sut = BuildResolver(out _);

        // 12-char but with letter — fails IsAsciiDigit check.
        var result = await sut.ResolveAsync(BuildRequest(taxId: "01234ABC9012"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.VendorId);
    }

    [Fact]
    public async Task InvalidShape_TaxIdTooShort_ReturnsNotApplicable()
    {
        var sut = BuildResolver(out _);

        var result = await sut.ResolveAsync(BuildRequest(taxId: "12345"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.VendorId);
    }

    [Fact]
    public async Task ExistingVendor_ReturnsExistingId()
    {
        var sut = BuildResolver(out var fakeRepo);
        var existing = Vendor.Create(Guid.NewGuid(), "0123456789", "Existing Co").Value;
        fakeRepo.Seed(existing);

        var result = await sut.ResolveAsync(new VendorLinkRequest(
            TenantId: existing.IdTenant,
            VendorTaxId: "0123456789",
            VendorName: "OCR Different Name",
            CreatedByMembershipId: Guid.NewGuid(),
            SourceDocumentId: Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value.VendorId);
        Assert.False(result.Value.WasAutoCreated);
    }

    [Fact]
    public async Task NewTaxCode_AutoCreatesVendor_AndReturnsAutoCreatedFlag()
    {
        var sut = BuildResolver(out var fakeRepo);
        var tenantId = Guid.NewGuid();

        var result = await sut.ResolveAsync(new VendorLinkRequest(
            TenantId: tenantId,
            VendorTaxId: "0987654321",
            VendorName: "Brand New Vendor",
            CreatedByMembershipId: Guid.NewGuid(),
            SourceDocumentId: Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.VendorId);
        Assert.True(result.Value.WasAutoCreated);

        var inserted = fakeRepo.AddedVendors.Single();
        Assert.Equal("0987654321", inserted.TaxCode);
        Assert.Equal("Brand New Vendor", inserted.Name);
        Assert.False(inserted.IsVerified);
    }

    [Fact]
    public async Task RaceCondition_DuplicateInsert_RecoversByRefetch()
    {
        // Simulate: SaveChanges throws on first call (other tx inserted same
        // tax code first). Resolver should detach + re-fetch winner.
        var winner = Vendor.Create(Guid.NewGuid(), "0123456789", "Winner Co").Value;
        var fakeRepo = new RaceFakeVendorRepository(winner);
        var fakeUow = new ThrowOnSaveUnitOfWork();
        var sut = new VendorLinkResolver(fakeRepo, fakeUow, NullLogger<VendorLinkResolver>.Instance);

        var result = await sut.ResolveAsync(new VendorLinkRequest(
            TenantId: winner.IdTenant,
            VendorTaxId: "0123456789",
            VendorName: "My OCR Name",
            CreatedByMembershipId: Guid.NewGuid(),
            SourceDocumentId: Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(winner.Id, result.Value.VendorId);
        Assert.False(result.Value.WasAutoCreated);   // links to existing winner
        Assert.True(fakeRepo.DetachCalled);
    }

    [Fact]
    public async Task TenantIdEmpty_ReturnsTenantRequired()
    {
        var sut = BuildResolver(out _);

        var result = await sut.ResolveAsync(new VendorLinkRequest(
            TenantId: Guid.Empty,
            VendorTaxId: "0123456789",
            VendorName: "X",
            CreatedByMembershipId: Guid.NewGuid(),
            SourceDocumentId: Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(VendorErrors.TenantRequired, result.Error);
    }

    private static VendorLinkResolver BuildResolver(out FakeVendorRepository repo)
    {
        repo = new FakeVendorRepository();
        return new VendorLinkResolver(repo, new NoOpUnitOfWork(), NullLogger<VendorLinkResolver>.Instance);
    }

    private static VendorLinkRequest BuildRequest(string? taxId) => new(
        TenantId: Guid.NewGuid(),
        VendorTaxId: taxId,
        VendorName: "Vendor X",
        CreatedByMembershipId: Guid.NewGuid(),
        SourceDocumentId: Guid.NewGuid());

    // ─────────────────────────────────────────── fakes
    private class FakeVendorRepository : IVendorRepository
    {
        public List<Vendor> AddedVendors { get; } = [];
        protected readonly Dictionary<(Guid Tenant, string TaxCode), Vendor> ByCode = [];

        public void Seed(Vendor v) => ByCode[(v.IdTenant, v.TaxCode)] = v;

        public virtual Task<Vendor?> GetEntityByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(ByCode.GetValueOrDefault((tenantId, taxCode)));

        public virtual Task<bool> ExistsByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(ByCode.ContainsKey((tenantId, taxCode)));

        public virtual void Add(Vendor vendor) => AddedVendors.Add(vendor);
        public virtual void Update(Vendor vendor) { }
        public virtual void Detach(Vendor vendor) { }

        public Task<VendorSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VendorSummary?> GetByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<VendorSummary>> GetAllAsync(Guid tenantId, bool? isVerified = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Vendor?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// On first save: SaveChanges throws (simulating unique-constraint race).
    /// On the re-fetch immediately after, returns the "winner" vendor that
    /// another transaction inserted.
    /// </summary>
    private sealed class RaceFakeVendorRepository : FakeVendorRepository
    {
        private readonly Vendor _winner;
        private bool _winnerVisible;
        public bool DetachCalled { get; private set; }

        public RaceFakeVendorRepository(Vendor winner) => _winner = winner;

        public override Task<Vendor?> GetEntityByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<Vendor?>(_winnerVisible && _winner.TaxCode == taxCode && _winner.IdTenant == tenantId ? _winner : null);

        public override void Detach(Vendor vendor)
        {
            DetachCalled = true;
            _winnerVisible = true;   // After detach, winner is visible to next read.
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class ThrowOnSaveUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated unique-constraint violation");
    }
}
