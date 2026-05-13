using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.Commands.ApproveReviewedDocument;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class ApproveReviewedDocumentCommandHandlerTests
{
    [Fact]
    public async Task Handle_IndexesReviewedDocument_AfterSuccessfulApproval()
    {
        var reviewedDocumentRepository = new Mock<IReviewedDocumentRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();
        var logger = new Mock<ILogger<ApproveReviewedDocumentCommandHandler>>();

        var document = CreateReviewedDocument();

        reviewedDocumentRepository
            .Setup(x => x.GetByIdForUpdateAsync(document.Id, document.IdTenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        indexer.Setup(x => x.ReindexAsync(document, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var sut = new ApproveReviewedDocumentCommandHandler(
            reviewedDocumentRepository.Object,
            unitOfWork.Object,
            indexer.Object,
            logger.Object);

        var result = await sut.Handle(
            new ApproveReviewedDocumentCommand(document.Id, document.IdTenant, Guid.NewGuid(), null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        indexer.Verify(x => x.ReindexAsync(document, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenAutoIndexFails()
    {
        var reviewedDocumentRepository = new Mock<IReviewedDocumentRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();
        var logger = new Mock<ILogger<ApproveReviewedDocumentCommandHandler>>();

        var document = CreateReviewedDocument();

        reviewedDocumentRepository
            .Setup(x => x.GetByIdForUpdateAsync(document.Id, document.IdTenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        indexer
            .Setup(x => x.ReindexAsync(document, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding failed"));

        var sut = new ApproveReviewedDocumentCommandHandler(
            reviewedDocumentRepository.Object,
            unitOfWork.Object,
            indexer.Object,
            logger.Object);

        var result = await sut.Handle(
            new ApproveReviewedDocumentCommand(document.Id, document.IdTenant, Guid.NewGuid(), null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static ReviewedDocument CreateReviewedDocument()
    {
        var submitterMembershipId = Guid.NewGuid();
        var lineItem = ReviewedDocumentLineItem.Create("Laptop bag", 1m, 1500000m, 1500000m);
        return ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            submitterMembershipId,
            "receipt.pdf",
            "application/pdf",
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
            [lineItem]).Value;
    }
}
