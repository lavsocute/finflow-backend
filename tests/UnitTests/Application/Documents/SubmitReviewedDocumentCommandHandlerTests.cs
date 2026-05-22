using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.Commands.SubmitReviewedDocument;
using FinFlow.Application.Documents.Duplicates;
using FinFlow.Application.Vendors.Services;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Vendors;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class SubmitReviewedDocumentCommandHandlerTests
{
    [Fact]
    public async Task Handle_IndexesReviewedDocument_AfterSuccessfulSubmit()
    {
        var sut = BuildSut(out var indexer, out _, out _);

        var result = await sut.Handler.Handle(CreateCommand(sut.TenantId, sut.MembershipId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        indexer.Verify(x => x.ReindexAsync(It.IsAny<ReviewedDocument>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenAutoIndexFails()
    {
        var sut = BuildSut(out var indexer, out _, out _);
        indexer
            .Setup(x => x.ReindexAsync(It.IsAny<ReviewedDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding failed"));

        var result = await sut.Handler.Handle(CreateCommand(sut.TenantId, sut.MembershipId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_DoesNotRejectSubmit_WhenVendorTaxIdIsNew()
    {
        // Behavior change: previously this scenario returned VendorErrors.NotFound;
        // now the resolver auto-creates the vendor and the submit succeeds.
        var sut = BuildSut(out _, out var resolver, out _);
        resolver
            .Setup(x => x.ResolveAsync(It.IsAny<VendorLinkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(VendorLinkResult.AutoCreated(Guid.NewGuid())));

        var cmd = CreateCommand(sut.TenantId, sut.MembershipId, vendorTaxId: "0123456789");
        var result = await sut.Handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        resolver.Verify(x => x.ResolveAsync(
            It.Is<VendorLinkRequest>(r => r.VendorTaxId == "0123456789" && r.TenantId == sut.TenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PassesEmptyTaxIdToResolver_WhichReturnsNotApplicable()
    {
        var sut = BuildSut(out _, out var resolver, out _);

        var cmd = CreateCommand(sut.TenantId, sut.MembershipId, vendorTaxId: null);
        var result = await sut.Handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        resolver.Verify(x => x.ResolveAsync(
            It.Is<VendorLinkRequest>(r => r.VendorTaxId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UsesCanonicalVendorName_FromVendorMaster_WhenLinked()
    {
        var sut = BuildSut(out _, out var resolver, out var vendorRepository);
        var vendorId = Guid.NewGuid();

        resolver
            .Setup(x => x.ResolveAsync(It.IsAny<VendorLinkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(VendorLinkResult.Existing(vendorId)));

        vendorRepository
            .Setup(x => x.GetByIdAsync(vendorId, sut.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VendorSummary(
                vendorId,
                sut.TenantId,
                "0123456789",
                "Bách Hóa Xanh",
                false,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow));

        var result = await sut.Handler.Handle(
            CreateCommand(sut.TenantId, sut.MembershipId, vendorTaxId: "0123456789"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Bách Hóa Xanh", result.Value.VendorName);
    }

    private static (SubmitReviewedDocumentCommandHandler Handler, Guid TenantId, Guid MembershipId) BuildSut(
        out Mock<IReviewedDocumentChunkIndexer> indexerMock,
        out Mock<IVendorLinkResolver> resolverMock,
        out Mock<IVendorRepository> vendorRepositoryMock)
    {
        var reviewedDocumentRepository = new Mock<IReviewedDocumentRepository>();
        var draftRepository = new Mock<IUploadedDocumentDraftRepository>();
        var membershipRepository = new Mock<ITenantMembershipRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();
        var resolver = new Mock<IVendorLinkResolver>();
        var vendorRepository = new Mock<IVendorRepository>();
        var logger = new Mock<ILogger<SubmitReviewedDocumentCommandHandler>>();
        var detector = new Mock<IDuplicateReceiptDetector>();

        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        membershipRepository
            .Setup(x => x.GetByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembershipSummary(
                membershipId, Guid.NewGuid(), tenantId, departmentId,
                RoleType.Staff, false, true, DateTime.UtcNow, null, null, null));

        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        indexer.Setup(x => x.ReindexAsync(It.IsAny<ReviewedDocument>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);

        // Default resolver: NotApplicable (no link). Tests that need linkage override this.
        resolver
            .Setup(x => x.ResolveAsync(It.IsAny<VendorLinkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(VendorLinkResult.NotApplicable));

        // Default detector: no duplicates found.
        detector
            .Setup(x => x.FindMatchesAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var handler = new SubmitReviewedDocumentCommandHandler(
            reviewedDocumentRepository.Object,
            draftRepository.Object,
            membershipRepository.Object,
            resolver.Object,
            detector.Object,
            unitOfWork.Object,
            indexer.Object,
            new FinFlow.UnitTests.TestStubs.StubTenantRepository(),
            new FinFlow.UnitTests.TestStubs.StubExchangeRateService(),
            logger.Object,
            vendorRepository.Object);

        indexerMock = indexer;
        resolverMock = resolver;
        vendorRepositoryMock = vendorRepository;
        return (handler, tenantId, membershipId);
    }

    private static SubmitReviewedDocumentCommand CreateCommand(Guid tenantId, Guid membershipId, string? vendorTaxId = null) =>
        new(
            null,
            tenantId,
            membershipId,
            "receipt.pdf",
            "Acme Supplies",
            "INV-001",
            new DateOnly(2026, 5, 10),
            "Equipment",
            vendorTaxId,
            1500000m,
            0m,
            1500000m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.UtcNow,
            [new SubmitReviewedDocumentLineItem("Laptop bag", 1m, 1500000m, 1500000m)]);
}
