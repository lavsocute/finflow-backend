using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Vendors.Commands.CreateVendor;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Vendors;

namespace FinFlow.UnitTests.Application.Vendors;

public sealed class CreateVendorCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsSuccess_WhenValidRequest()
    {
        var repository = new StubVendorRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new CreateVendorCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new CreateVendorCommand(Guid.NewGuid(), "0123456789", "Test Vendor"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        Assert.Single(repository.AddedVendors);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsTaxCodeExists_WhenVendorAlreadyExists()
    {
        var repository = new StubVendorRepository(existsByTaxCode: true);
        var unitOfWork = new StubUnitOfWork();
        var handler = new CreateVendorCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new CreateVendorCommand(Guid.NewGuid(), "0123456789", "Test Vendor"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Vendor.TaxCodeExists", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WithNormalizedData()
    {
        var repository = new StubVendorRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new CreateVendorCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new CreateVendorCommand(Guid.NewGuid(), "  0123456789  ", "  Test Vendor  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var vendor = repository.AddedVendors[0];
        Assert.Equal("0123456789", vendor.TaxCode);
        Assert.Equal("Test Vendor", vendor.Name);
    }

    private sealed class StubVendorRepository : IVendorRepository
    {
        private readonly bool _existsByTaxCode;

        public StubVendorRepository(bool existsByTaxCode = false)
        {
            _existsByTaxCode = existsByTaxCode;
        }

        public List<Vendor> AddedVendors { get; } = [];

        public Task<VendorSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<VendorSummary?>(null);

        public Task<VendorSummary?> GetByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<VendorSummary?>(null);

        public Task<bool> ExistsByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(_existsByTaxCode);

        public Task<IReadOnlyList<VendorSummary>> GetAllAsync(Guid tenantId, bool? isVerified = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<VendorSummary>>([]);

        public Task<Vendor?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<Vendor?>(null);

        public Task<Vendor?> GetEntityByTaxCodeAsync(string taxCode, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<Vendor?>(null);

        public void Add(Vendor vendor) => AddedVendors.Add(vendor);
        public void Update(Vendor vendor) { }
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