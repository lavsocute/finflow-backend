using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using PDFtoImage;
using SkiaSharp;

namespace FinFlow.Infrastructure.Ocr.Pdf;

public sealed class PdfPageRenderer : IPdfPageRenderer
{
    public Task<Result<IReadOnlyList<OcrPageImage>>> RenderAsync(
        byte[] pdfBytes,
        int maxPages,
        CancellationToken cancellationToken)
    {
        if (pdfBytes.Length == 0 || maxPages <= 0)
            return Task.FromResult(Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrPdfRenderFailed));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!OperatingSystem.IsWindows()
                && !OperatingSystem.IsLinux()
                && !OperatingSystem.IsMacOS())
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrPdfRenderFailed));
            }

            var pageCount = Conversion.GetPageCount(pdfBytes, null);
            var pagesToRender = Math.Min(pageCount, maxPages);
            var renderedPages = new List<OcrPageImage>(pagesToRender);

            for (var pageIndex = 0; pageIndex < pagesToRender; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var bitmap = Conversion.ToImage(pdfBytes, pageIndex, null, new RenderOptions());
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);

                if (encoded is null)
                    return Task.FromResult(Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrPdfRenderFailed));

                renderedPages.Add(new OcrPageImage(
                    pageIndex + 1,
                    "image/png",
                    Convert.ToBase64String(encoded.ToArray())));
            }

            return Task.FromResult(Result.Success<IReadOnlyList<OcrPageImage>>(renderedPages));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrPdfRenderFailed));
        }
    }
}
