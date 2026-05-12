using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using Moq;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class ReviewedDocumentChunkIndexerTests
{
    [Fact]
    public async Task ReindexAsync_RebuildsExpenseAndReceiptChunks_ForReviewedDocument()
    {
        var document = CreateReviewedDocument();
        var chunkingService = new Mock<IChunkingService>();
        var vectorStore = new Mock<IVectorStore>();

        chunkingService
            .Setup(x => x.ChunkAsync(
                document.IdTenant,
                It.IsAny<string>(),
                DocumentChunkType.Expense,
                document.Id,
                document.MembershipId,
                document.IdDepartment,
                500,
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateChunk(document, DocumentChunkType.Expense)]);

        chunkingService
            .Setup(x => x.ChunkAsync(
                document.IdTenant,
                It.IsAny<string>(),
                DocumentChunkType.Receipt,
                document.Id,
                document.MembershipId,
                document.IdDepartment,
                500,
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateChunk(document, DocumentChunkType.Receipt)]);

        var sut = new ReviewedDocumentChunkIndexer(chunkingService.Object, vectorStore.Object);

        var result = await sut.ReindexAsync(document, CancellationToken.None);

        Assert.Equal(2, result);
        vectorStore.Verify(x => x.DeleteByDocumentIdAsync(document.Id, It.IsAny<CancellationToken>()), Times.Once);
        vectorStore.Verify(x => x.UpsertChunksAsync(
            It.Is<IEnumerable<DocumentChunk>>(chunks =>
                chunks.Count() == 2 &&
                chunks.Any(chunk => chunk.Type == DocumentChunkType.Expense) &&
                chunks.Any(chunk => chunk.Type == DocumentChunkType.Receipt)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ReviewedDocument CreateReviewedDocument()
    {
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        var lineItem = ReviewedDocumentLineItem.Create("Laptop bag", 1m, 1500000m, 1500000m);
        return ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            tenantId,
            departmentId,
            membershipId,
            "receipt.pdf",
            "application/pdf",
            "Acme Supplies",
            "INV-001",
            new DateOnly(2026, 5, 10),
            "Equipment",
            "TAX-001",
            1500000m,
            0m,
            1500000m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.UtcNow,
            [lineItem]).Value;
    }

    private static DocumentChunk CreateChunk(ReviewedDocument document, DocumentChunkType type) =>
        DocumentChunk.Create(
            document.IdTenant,
            document.MembershipId,
            document.Id,
            document.IdDepartment,
            $"content-{type}",
            $"hash-{type}",
            0,
            [0.1f, 0.2f],
            type);
}
