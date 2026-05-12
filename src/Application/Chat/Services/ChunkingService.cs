using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;
using System.Security.Cryptography;
using System.Text;

namespace FinFlow.Application.Chat.Services;

public sealed class ChunkingService : IChunkingService
{
    private readonly IEmbeddingService _embeddingService;

    public ChunkingService(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        Guid tenantId,
        string text,
        DocumentChunkType type,
        Guid documentId,
        Guid ownerMembershipId,
        Guid? departmentId,
        int chunkSize = 500,
        int overlap = 50,
        CancellationToken ct = default)
    {
        var chunks = new List<DocumentChunk>();

        var sentences = SplitIntoSentences(text);
        var currentChunk = new StringBuilder();

        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            if (currentChunk.Length + sentence.Length > chunkSize && currentChunk.Length > 0)
            {
                var content = currentChunk.ToString().Trim();
                var embedding = await _embeddingService.EmbedAsync(content, ct);

                chunks.Add(DocumentChunk.Create(
                    tenantId,
                    ownerMembershipId,
                    documentId,
                    departmentId,
                    content,
                    ComputeHash(content),
                    chunks.Count,
                    embedding,
                    type));

                var overlapText = currentChunk.ToString().Substring(Math.Max(0, currentChunk.Length - overlap));
                currentChunk.Clear();
                if (overlapText.Length > 0)
                {
                    currentChunk.Append(overlapText);
                }
            }

            currentChunk.Append(sentence);
            if (currentChunk.Length < chunkSize)
            {
                currentChunk.Append(" ");
            }
        }

        if (currentChunk.Length > 0)
        {
            var content = currentChunk.ToString().Trim();
            var embedding = await _embeddingService.EmbedAsync(content, ct);

            chunks.Add(DocumentChunk.Create(
                tenantId,
                ownerMembershipId,
                documentId,
                departmentId,
                content,
                ComputeHash(content),
                chunks.Count,
                embedding,
                type));
        }

        return chunks;
    }

    private static List<string> SplitIntoSentences(string text)
    {
        return text
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split(new[] { ". ", ".\n", "! ", "!\n", "? ", "?\n", ".\n\n" }, StringSplitOptions.None)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
