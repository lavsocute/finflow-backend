namespace FinFlow.Application.Common.Abstractions;

public interface IDocumentStorageProvider
{
    Task SaveImageAsync(Guid documentId, byte[] imageData, string contentType, CancellationToken cancellationToken = default);
    Task<byte[]?> GetImageAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task DeleteImageAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<string?> GetContentTypeAsync(Guid documentId, CancellationToken cancellationToken = default);
}
