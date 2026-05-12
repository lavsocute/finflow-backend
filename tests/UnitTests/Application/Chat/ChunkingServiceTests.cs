using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Documents;
using Moq;

namespace FinFlow.UnitTests.Application.Chat;

public class ChunkingServiceTests
{
    [Fact]
    public async Task ChunkAsync_AssignsProvidedTenantIdToAllChunks()
    {
        var embeddingService = new Mock<IEmbeddingService>();
        embeddingService
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 0.1f, 0.2f, 0.3f });

        var service = new ChunkingService(embeddingService.Object);
        var tenantId = Guid.NewGuid();
        var ownerMembershipId = Guid.NewGuid();

        var chunks = await service.ChunkAsync(
            tenantId,
            "Sentence one. Sentence two. Sentence three.",
            DocumentChunkType.Receipt,
            Guid.NewGuid(),
            ownerMembershipId,
            Guid.NewGuid(),
            chunkSize: 20,
            overlap: 5);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.Equal(tenantId, chunk.IdTenant));
        Assert.All(chunks, chunk => Assert.Equal(ownerMembershipId, chunk.OwnerMembershipId));
    }
}

