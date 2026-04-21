using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Documents;

public sealed class PostgresDocumentStorageProvider : IDocumentStorageProvider
{
    private readonly ApplicationDbContext _dbContext;

    public PostgresDocumentStorageProvider(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveImageAsync(Guid documentId, byte[] imageData, string contentType, CancellationToken cancellationToken = default)
    {
        var draft = await _dbContext.Set<UploadedDocumentDraft>()
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (draft is null)
            throw new InvalidOperationException($"Document with ID {documentId} not found.");

        var imageDataProperty = _dbContext.Entry(draft).Property(x => x.ImageData);
        var imageContentTypeProperty = _dbContext.Entry(draft).Property(x => x.ImageContentType);

        imageDataProperty.CurrentValue = imageData;
        imageContentTypeProperty.CurrentValue = contentType;
    }

    public async Task<byte[]?> GetImageAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<UploadedDocumentDraft>()
            .Where(x => x.Id == documentId)
            .Select(x => x.ImageData)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task DeleteImageAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var draft = await _dbContext.Set<UploadedDocumentDraft>()
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (draft is null)
            throw new InvalidOperationException($"Document with ID {documentId} not found.");

        var imageDataProperty = _dbContext.Entry(draft).Property(x => x.ImageData);
        var imageContentTypeProperty = _dbContext.Entry(draft).Property(x => x.ImageContentType);

        imageDataProperty.CurrentValue = null;
        imageContentTypeProperty.CurrentValue = null;
    }

    public async Task<string?> GetContentTypeAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<UploadedDocumentDraft>()
            .Where(x => x.Id == documentId)
            .Select(x => x.ImageContentType)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
