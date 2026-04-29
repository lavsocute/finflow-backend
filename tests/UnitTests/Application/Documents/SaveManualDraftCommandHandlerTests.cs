using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Commands.SaveManualDraft;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class SaveManualDraftCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsSuccess_WhenValidRequest()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 1m, 1000m, 1000m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        Assert.Single(repository.AddedDrafts);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WithNormalizedData()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "  invoice.pdf  ",
                "  Vendor Co  ",
                "  INV-001  ",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "  Office Supplies  ",
                "  TX-123  ",
                1000m,
                100m,
                1100m,
                "  staff@finflow.test  ",
                [
                    new SaveManualDraftLineItem("  Item 1  ", 1m, 1000m, 1000m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var draft = repository.AddedDrafts[0];
        Assert.Equal("invoice.pdf", draft.OriginalFileName);
        Assert.Equal("Vendor Co", draft.VendorName);
        Assert.Equal("INV-001", draft.Reference);
        Assert.Equal("Office Supplies", draft.Category);
        Assert.Equal("TX-123", draft.VendorTaxId);
        Assert.Equal("staff@finflow.test", draft.UploadedByStaff);
    }

    [Fact]
    public async Task Handle_ReturnsVendorNameRequired_WhenVendorNameIsBlank()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "   ",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 1m, 1000m, 1000m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.VendorNameRequired", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsReferenceRequired_WhenReferenceIsBlank()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "   ",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 1m, 1000m, 1000m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.ReferenceRequired", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsCategoryRequired_WhenCategoryIsBlank()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "   ",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 1m, 1000m, 1000m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.CategoryRequired", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsTotalAmountInvalid_WhenTotalAmountIsZero()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                0m,
                0m,
                0m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 1m, 1000m, 1000m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.TotalAmountInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsTotalAmountInvalid_WhenTotalAmountIsNegative()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                -100m,
                0m,
                -100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 1m, 1000m, 1000m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.TotalAmountInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsLineItemRequired_WhenNoLineItems()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                []),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.LineItemRequired", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsLineItemNameRequired_WhenLineItemNameIsBlank()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("   ", 1m, 1000m, 1000m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.LineItemNameRequired", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsLineItemQuantityInvalid_WhenQuantityIsZero()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 0m, 1000m, 0m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.LineItemQuantityInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsLineItemUnitPriceInvalid_WhenUnitPriceIsZero()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 1m, 0m, 0m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.LineItemUnitPriceInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsLineItemTotalInvalid_WhenTotalIsZero()
    {
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new SaveManualDraftCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new SaveManualDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "Vendor Co",
                "INV-001",
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 5, 1),
                "Office Supplies",
                "TX-123",
                1000m,
                100m,
                1100m,
                "staff@finflow.test",
                [
                    new SaveManualDraftLineItem("Item 1", 1m, 1000m, 0m)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("UploadedDocumentDraft.LineItemTotalInvalid", result.Error.Code);
    }

    private sealed class StubUploadedDocumentDraftRepository : IUploadedDocumentDraftRepository
    {
        private readonly UploadedDocumentDraft? _getByIdResult;

        public StubUploadedDocumentDraftRepository(UploadedDocumentDraft? getByIdResult = null)
        {
            _getByIdResult = getByIdResult;
        }

        public List<UploadedDocumentDraft> AddedDrafts { get; } = [];

        public void Add(UploadedDocumentDraft draft) => AddedDrafts.Add(draft);
        public void Update(UploadedDocumentDraft draft) { }
        public Task<UploadedDocumentDraft?> GetByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default)
            => Task.FromResult(_getByIdResult);
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_getByIdResult != null);
        public Task<IReadOnlyList<UploadedDocumentDraft>> GetOwnedActiveAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UploadedDocumentDraft>>([]);
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
