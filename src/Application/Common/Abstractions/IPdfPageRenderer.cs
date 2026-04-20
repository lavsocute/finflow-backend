using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Common.Abstractions;

public interface IPdfPageRenderer
{
    Task<Result<IReadOnlyList<OcrPageImage>>> RenderAsync(
        byte[] pdfBytes,
        int maxPages,
        CancellationToken cancellationToken);
}
