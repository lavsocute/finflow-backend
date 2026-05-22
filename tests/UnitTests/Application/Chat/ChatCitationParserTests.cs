using FinFlow.Application.Chat.Services;
using FinFlow.Domain.Documents;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class ChatCitationParserTests
{
    private static DocumentChunk MakeChunk(int n) =>
        DocumentChunk.Create(
            tenantId: Guid.NewGuid(),
            ownerMembershipId: Guid.NewGuid(),
            documentId: Guid.NewGuid(),
            departmentId: Guid.NewGuid(),
            content: $"Chunk content number {n} with vendor info that is fairly long but readable.",
            contentHash: $"hash-{n}",
            chunkIndex: 0,
            embedding: new float[] { 0.1f, 0.2f, 0.3f },
            type: DocumentChunkType.Expense);

    [Fact]
    public void Parse_NoMarkers_ReturnsEmpty()
    {
        var chunks = new[] { MakeChunk(1), MakeChunk(2) };

        var citations = ChatCitationParser.Parse("Plain answer without any markers.", chunks);

        Assert.Empty(citations);
    }

    [Fact]
    public void Parse_SingleMarker_ReturnsOneCitation()
    {
        var chunks = new[] { MakeChunk(1), MakeChunk(2) };

        var citations = ChatCitationParser.Parse("Total spending was 5M VND [chunk-1].", chunks);

        Assert.Single(citations);
        Assert.Equal(1, citations[0].ChunkNumber);
        Assert.Equal(chunks[0].Id, citations[0].ChunkId);
    }

    [Fact]
    public void Parse_MultipleMarkers_ReturnsAllUnique()
    {
        var chunks = new[] { MakeChunk(1), MakeChunk(2), MakeChunk(3) };

        var citations = ChatCitationParser.Parse(
            "Vendor A spent X [chunk-1]. Vendor B spent Y [chunk-2]. Total [chunk-1] and [chunk-3].",
            chunks);

        Assert.Equal(3, citations.Count);
        Assert.Contains(citations, c => c.ChunkNumber == 1);
        Assert.Contains(citations, c => c.ChunkNumber == 2);
        Assert.Contains(citations, c => c.ChunkNumber == 3);
    }

    [Fact]
    public void Parse_OutOfRangeMarker_IsIgnored()
    {
        var chunks = new[] { MakeChunk(1) };

        var citations = ChatCitationParser.Parse("[chunk-1] and [chunk-99]", chunks);

        Assert.Single(citations);
        Assert.Equal(1, citations[0].ChunkNumber);
    }

    [Fact]
    public void Parse_PreviewTruncated()
    {
        var chunks = new[] { MakeChunk(1) };

        var citations = ChatCitationParser.Parse("[chunk-1]", chunks);

        Assert.True(citations[0].Preview.Length <= 103); // 100 + "..."
    }

    [Fact]
    public void StripMarkers_RemovesChunkLabels_AndNormalizesSpacing()
    {
        var answer = "Tổng chi là 5.000.000 VND [chunk-1]. Nhà cung cấp lớn nhất là A [chunk-2], và xu hướng tăng [chunk-3].";

        var cleaned = ChatCitationParser.StripMarkers(answer);

        Assert.DoesNotContain("[chunk-", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Tổng chi là 5.000.000 VND. Nhà cung cấp lớn nhất là A, và xu hướng tăng.", cleaned);
    }
}
