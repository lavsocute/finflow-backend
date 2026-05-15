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
        // Phase 1: Split text into chunk contents (no async work).
        var chunkContents = new List<string>();
        var sentences = SplitIntoSentences(text);
        var currentChunk = new StringBuilder();

        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            if (currentChunk.Length + sentence.Length > chunkSize && currentChunk.Length > 0)
            {
                chunkContents.Add(currentChunk.ToString().Trim());

                var overlapStart = Math.Max(0, currentChunk.Length - overlap);
                var overlapText = currentChunk.ToString()[overlapStart..];
                currentChunk.Clear();
                currentChunk.Append(overlapText);
            }

            currentChunk.Append(sentence);
            if (currentChunk.Length < chunkSize)
            {
                currentChunk.Append(' ');
            }
        }

        if (currentChunk.Length > 0)
        {
            chunkContents.Add(currentChunk.ToString().Trim());
        }

        if (chunkContents.Count == 0)
            return [];

        // Phase 2: Generate embeddings in parallel (bounded concurrency).
        const int maxConcurrency = 5;
        var semaphore = new SemaphoreSlim(maxConcurrency);
        var embeddingTasks = chunkContents.Select(async content =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await _embeddingService.EmbedAsync(content, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var embeddings = await Task.WhenAll(embeddingTasks);

        // Phase 3: Build DocumentChunk entities.
        var chunks = new List<DocumentChunk>(chunkContents.Count);
        for (int i = 0; i < chunkContents.Count; i++)
        {
            chunks.Add(DocumentChunk.Create(
                tenantId,
                ownerMembershipId,
                documentId,
                departmentId,
                chunkContents[i],
                ComputeHash(chunkContents[i]),
                i,
                embeddings[i],
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
