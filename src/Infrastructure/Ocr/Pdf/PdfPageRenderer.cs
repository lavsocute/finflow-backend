using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;

namespace FinFlow.Infrastructure.Ocr.Pdf;

public sealed class PdfPageRenderer : IPdfPageRenderer
{
    public Task<Result<IReadOnlyList<OcrPageImage>>> RenderAsync(
        byte[] pdfBytes,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrPdfRenderFailed));
}
