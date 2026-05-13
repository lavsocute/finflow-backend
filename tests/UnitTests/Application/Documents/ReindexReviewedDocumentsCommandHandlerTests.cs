using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.Commands.ReindexReviewedDocuments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class ReindexReviewedDocumentsCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReindexesAllTenantDocuments_AndCountsFailures()
    {
        var tenantId = Guid.NewGuid();
        var documentA = CreateReviewedDocument(tenantId);
        var documentB = CreateReviewedDocument(tenantId);

        var repository = new Mock<IReviewedDocumentRepository>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();
        var logger = new Mock<ILogger<ReindexReviewedDocumentsCommandHandler>>();

        repository
            .Setup(x => x.GetAllActiveByTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([documentA, documentB]);

        indexer
            .Setup(x => x.ReindexAsync(documentA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        indexer
            .Setup(x => x.ReindexAsync(documentB, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("failed"));

        var sut = new ReindexReviewedDocumentsCommandHandler(
            repository.Object,
            indexer.Object,
            logger.Object);

        var result = await sut.Handle(
            new ReindexReviewedDocumentsCommand(tenantId, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.ScannedDocuments);
        Assert.Equal(1, result.Value.IndexedDocuments);
        Assert.Equal(1, result.Value.FailedDocuments);
        Assert.Equal(2, result.Value.TotalChunks);
    }

    private static ReviewedDocument CreateReviewedDocument(Guid tenantId)
    {
        var lineItem = ReviewedDocumentLineItem.Create("Laptop bag", 1m, 1500000m, 1500000m);
        return ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
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
