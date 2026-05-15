using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Common.Abstractions;

public interface IPdfPageRenderer
{
    Task<Result<PdfRenderResult>> RenderAsync(
        byte[] pdfBytes,
        int maxPages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts pages without rendering them. Use this for quota validation
    /// before performing the expensive render step.
    /// </summary>
    Task<Result<int>> GetPageCountAsync(byte[] pdfBytes, CancellationToken cancellationToken);
}
