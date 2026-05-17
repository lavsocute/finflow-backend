using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.Commands.SubmitReviewedDocument;
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
        var reviewedDocumentRepository = new Mock<IReviewedDocumentRepository>();
        var draftRepository = new Mock<IUploadedDocumentDraftRepository>();
        var membershipRepository = new Mock<ITenantMembershipRepository>();
        var vendorRepository = new Mock<IVendorRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();
        var logger = new Mock<ILogger<SubmitReviewedDocumentCommandHandler>>();

        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        membershipRepository
            .Setup(x => x.GetByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                departmentId,
                RoleType.Staff,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null));

        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        indexer.Setup(x => x.ReindexAsync(It.IsAny<ReviewedDocument>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var sut = new SubmitReviewedDocumentCommandHandler(
            reviewedDocumentRepository.Object,
            draftRepository.Object,
            membershipRepository.Object,
            vendorRepository.Object,
            unitOfWork.Object, indexer.Object, new FinFlow.UnitTests.TestStubs.StubTenantRepository(), new FinFlow.UnitTests.TestStubs.StubExchangeRateService(), logger.Object);

        var result = await sut.Handle(CreateCommand(tenantId, membershipId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        indexer.Verify(x => x.ReindexAsync(It.IsAny<ReviewedDocument>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenAutoIndexFails()
    {
        var reviewedDocumentRepository = new Mock<IReviewedDocumentRepository>();
        var draftRepository = new Mock<IUploadedDocumentDraftRepository>();
        var membershipRepository = new Mock<ITenantMembershipRepository>();
        var vendorRepository = new Mock<IVendorRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();
        var logger = new Mock<ILogger<SubmitReviewedDocumentCommandHandler>>();

        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        membershipRepository
            .Setup(x => x.GetByIdAsync(membershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembershipSummary(
                membershipId,
                Guid.NewGuid(),
                tenantId,
                departmentId,
                RoleType.Staff,
                false,
                true,
                DateTime.UtcNow,
                null,
                null,
                null));

        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        indexer
            .Setup(x => x.ReindexAsync(It.IsAny<ReviewedDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding failed"));

        var sut = new SubmitReviewedDocumentCommandHandler(
            reviewedDocumentRepository.Object,
            draftRepository.Object,
            membershipRepository.Object,
            vendorRepository.Object,
            unitOfWork.Object, indexer.Object, new FinFlow.UnitTests.TestStubs.StubTenantRepository(), new FinFlow.UnitTests.TestStubs.StubExchangeRateService(), logger.Object);

        var result = await sut.Handle(CreateCommand(tenantId, membershipId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static SubmitReviewedDocumentCommand CreateCommand(Guid tenantId, Guid membershipId) =>
        new(
            null,
            tenantId,
            membershipId,
            "receipt.pdf",
            "Acme Supplies",
            "INV-001",
            new DateOnly(2026, 5, 10),
            "Equipment",
            null,
            1500000m,
            0m,
            1500000m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.UtcNow,
            [new SubmitReviewedDocumentLineItem("Laptop bag", 1m, 1500000m, 1500000m)]);
}
