using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Common.Abstractions;

public interface IOcrExtractionService
{
    Task<Result<OcrExtractionResult>> ExtractAsync(
        string fileName,
        string contentType,
        byte[] fileContents,
        CancellationToken cancellationToken);

    Task<Result<int>> GetPageCountAsync(
        string contentType,
        byte[] fileContents,
        CancellationToken cancellationToken);
}
