using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using Moq;
using System.Reflection;

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
        vectorStore.Verify(x => x.ReplaceDocumentChunksAsync(
            document.Id,
            It.Is<IEnumerable<DocumentChunk>>(chunks =>
                chunks.Count() == 2 &&
                chunks.Any(chunk => chunk.Type == DocumentChunkType.Expense) &&
                chunks.Any(chunk => chunk.Type == DocumentChunkType.Receipt)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReindexAsync_Should_Remain_Idempotent_For_Repeated_Reindex_Of_Same_Document()
    {
        var document = CreateReviewedDocument();
        var chunkingService = new Mock<IChunkingService>();
        var vectorStore = new RecordingVectorStore();

        chunkingService
            .Setup(x => x.ChunkAsync(
                document.IdTenant,
                It.IsAny<string>(),
                It.IsAny<DocumentChunkType>(),
                document.Id,
                document.MembershipId,
                document.IdDepartment,
                500,
                50,
                It.IsAny<CancellationToken>()))
            .Returns<Guid, string, DocumentChunkType, Guid, Guid, Guid?, int, int, CancellationToken>((_, _, type, _, _, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<DocumentChunk>>([CreateChunk(document, type)]));

        var sut = new ReviewedDocumentChunkIndexer(chunkingService.Object, vectorStore);

        var firstCount = await sut.ReindexAsync(document, CancellationToken.None);
        var secondCount = await sut.ReindexAsync(document, CancellationToken.None);

        Assert.Equal(2, firstCount);
        Assert.Equal(2, secondCount);
        Assert.Equal(2, vectorStore.StoredChunks.Count);
        Assert.Equal(2, vectorStore.DeleteCalls);
        Assert.Equal(2, vectorStore.UpsertCalls);
        Assert.Equal(
            ["content-Expense", "content-Receipt"],
            vectorStore.StoredChunks
                .OrderBy(chunk => chunk.Type)
                .Select(chunk => chunk.Content)
                .ToArray());
    }

    [Fact]
    public async Task ReindexAsync_Should_Reject_Empty_Department_Metadata_Before_Chunking()
    {
        var document = CreateReviewedDocument();
        typeof(ReviewedDocument).GetProperty(nameof(ReviewedDocument.IdDepartment), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(document, Guid.Empty);

        var chunkingService = new Mock<IChunkingService>();
        var vectorStore = new Mock<IVectorStore>();
        var sut = new ReviewedDocumentChunkIndexer(chunkingService.Object, vectorStore.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => sut.ReindexAsync(document, CancellationToken.None));

        Assert.Contains("department", ex.Message, StringComparison.OrdinalIgnoreCase);
        chunkingService.Verify(x => x.ChunkAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<DocumentChunkType>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
        vectorStore.Verify(x => x.DeleteByDocumentIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        vectorStore.Verify(x => x.UpsertChunksAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()), Times.Never);
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

    private sealed class RecordingVectorStore : IVectorStore
    {
        private readonly List<DocumentChunk> _storedChunks = [];

        public IReadOnlyList<DocumentChunk> StoredChunks => _storedChunks;
        public int DeleteCalls { get; private set; }
        public int UpsertCalls { get; private set; }

        public Task UpsertChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
        {
            UpsertCalls++;
            _storedChunks.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DocumentChunk>> SearchAsync(
            float[] queryEmbedding,
            Guid tenantId,
            Guid? departmentId,
            Guid? ownerId,
            IReadOnlyCollection<DocumentChunkType>? allowedTypes = null,
            int topK = 20,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default)
        {
            DeleteCalls++;
            _storedChunks.RemoveAll(chunk => chunk.DocumentId == documentId);
            return Task.CompletedTask;
        }

        public Task ReplaceDocumentChunksAsync(Guid documentId, IEnumerable<DocumentChunk> newChunks, CancellationToken ct = default)
        {
            DeleteCalls++;
            _storedChunks.RemoveAll(chunk => chunk.DocumentId == documentId);
            UpsertCalls++;
            _storedChunks.AddRange(newChunks);
            return Task.CompletedTask;
        }
    }
}
