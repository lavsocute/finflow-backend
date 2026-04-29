using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Vendors.Commands.VerifyVendor;
using FinFlow.Application.Vendors.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;

namespace FinFlow.UnitTests.Application.Vendors;

public sealed class VerifyVendorCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsSuccess_WhenVendorExists()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var vendor = Vendor.Create(tenantId, "0123456789", "Test Vendor").Value;
        var repository = new StubVendorRepository(getEntityResult: vendor);
        var unitOfWork = new StubUnitOfWork();
        var handler = new VerifyVendorCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new VerifyVendorCommand(vendor.Id, tenantId, membershipId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(vendor.Id, result.Value.VendorId);
        Assert.True(result.Value.IsVerified);
        Assert.Equal(membershipId, result.Value.VerifiedByMembershipId);
        Assert.NotNull(result.Value.VerifiedAt);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenVendorDoesNotExist()
    {
        var repository = new StubVendorRepository(getEntityResult: null);
        var unitOfWork = new StubUnitOfWork();
        var handler = new VerifyVendorCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new VerifyVendorCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsAlreadyVerified_WhenVendorAlreadyVerified()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var vendor = Vendor.Create(tenantId, "0123456789", "Test Vendor").Value;
        vendor.Verify(Guid.NewGuid());
        var repository = new StubVendorRepository(getEntityResult: vendor);
        var unitOfWork = new StubUnitOfWork();
        var handler = new VerifyVendorCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new VerifyVendorCommand(vendor.Id, tenantId, membershipId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.AlreadyVerified", result.Error.Code);
    }

    [Fact]
    public async Task Handle_CallsUpdate_WhenVerificationSucceeds()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var vendor = Vendor.Create(tenantId, "0123456789", "Test Vendor").Value;
        var repository = new StubVendorRepository(getEntityResult: vendor);
        var unitOfWork = new StubUnitOfWork();
        var handler = new VerifyVendorCommandHandler(repository, unitOfWork);

        await handler.Handle(
            new VerifyVendorCommand(vendor.Id, tenantId, membershipId),
            CancellationToken.None);

        Assert.Single(repository.UpdatedVendors);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    private sealed class StubVendorRepository : IVendorRepository
    {
        private readonly Vendor? _getEntityResult;

        public StubVendorRepository(Vendor? getEntityResult = null)
        {
            _getEntityResult = getEntityResult;
        }

        public List<Vendor> UpdatedVendors { get; } = [];

        public Task<VendorSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<VendorSummary?>(null);

        public Task<VendorSummary?> GetByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<VendorSummary?>(null);

        public Task<bool> ExistsByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<VendorSummary>> GetAllAsync(Guid tenantId, bool? isVerified = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<VendorSummary>>([]);

        public Task<Vendor?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(_getEntityResult);

        public Task<Vendor?> GetEntityByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<Vendor?>(null);

        public void Add(Vendor vendor) { }
        public void Update(Vendor vendor) => UpdatedVendors.Add(vendor);
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }
}