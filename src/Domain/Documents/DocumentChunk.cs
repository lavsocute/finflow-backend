using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Documents;

public enum DocumentChunkType
{
    Expense,
    Policy,
    Receipt,
    ApprovalFlow,
    Budget,
    Report,
    LineItem
}

public class DocumentChunk : Entity, IMultiTenant
{
    public Guid IdTenant { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid OwnerMembershipId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string Content { get; private set; }
    public string ContentHash { get; private set; }
    public int ChunkIndex { get; private set; }
    public float[] Embedding { get; private set; }
    public DocumentChunkType Type { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private DocumentChunk() { }

    public static DocumentChunk Create(
        Guid tenantId,
        Guid ownerMembershipId,
        Guid documentId,
        Guid? departmentId,
        string content,
        string contentHash,
        int chunkIndex,
        float[] embedding,
        DocumentChunkType type)
    {
        return new DocumentChunk
        {
            Id = Guid.NewGuid(),
            IdTenant = tenantId,
            OwnerMembershipId = ownerMembershipId,
            DocumentId = documentId,
            DepartmentId = departmentId,
            Content = content,
            ContentHash = contentHash,
            ChunkIndex = chunkIndex,
            Embedding = embedding,
            Type = type,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a new DocumentChunk with sanitized Content, preserving all other properties.
    /// Used to harden document chunks against prompt injection before RAG insertion.
    /// </summary>
    public static DocumentChunk WithSanitizedContent(DocumentChunk original, string sanitizedContent)
    {
        return new DocumentChunk
        {
            Id = original.Id,
            IdTenant = original.IdTenant,
            OwnerMembershipId = original.OwnerMembershipId,
            DocumentId = original.DocumentId,
            DepartmentId = original.DepartmentId,
            Content = sanitizedContent,
            ContentHash = original.ContentHash,
            ChunkIndex = original.ChunkIndex,
            Embedding = original.Embedding,
            Type = original.Type,
            CreatedAt = original.CreatedAt
        };
    }
}