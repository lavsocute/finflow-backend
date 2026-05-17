using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using PDFtoImage;
using SkiaSharp;

namespace FinFlow.Infrastructure.Ocr.Pdf;

public sealed class PdfPageRenderer : IPdfPageRenderer
{
    public Task<Result<PdfRenderResult>> RenderAsync(
        byte[] pdfBytes,
        int maxPages,
        CancellationToken cancellationToken)
    {
        if (pdfBytes.Length == 0 || maxPages <= 0)
            return Task.FromResult(Result.Failure<PdfRenderResult>(DocumentOcrErrors.OcrPdfRenderFailed));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!OperatingSystem.IsWindows()
                && !OperatingSystem.IsLinux()
                && !OperatingSystem.IsMacOS())
            {
                return Task.FromResult(Result.Failure<PdfRenderResult>(DocumentOcrErrors.OcrPdfRenderFailed));
            }

            var pageCount = Conversion.GetPageCount(pdfBytes, null);
            var pagesToRender = Math.Min(pageCount, maxPages);
            var renderedPages = new List<OcrPageImage>(pagesToRender);
            var failedPages = 0;

            for (var pageIndex = 0; pageIndex < pagesToRender; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var bitmap = Conversion.ToImage(pdfBytes, pageIndex, null, new RenderOptions());
                    using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);

                    if (encoded is null)
                    {
                        failedPages++;
                        continue;
                    }

                    renderedPages.Add(new OcrPageImage(
                        pageIndex + 1,
                        "image/png",
                        Convert.ToBase64String(encoded.ToArray())));
                }
                catch
                {
                    // Fix #7: per-page render error does not kill the whole document.
                    // If at least one page succeeds we surface a truncated result.
                    failedPages++;
                }
            }

            if (renderedPages.Count == 0)
                return Task.FromResult(Result.Failure<PdfRenderResult>(DocumentOcrErrors.OcrPdfRenderFailed));

            return Task.FromResult(Result.Success(PdfRenderResult.Success(renderedPages, pageCount)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Task.FromResult(Result.Failure<PdfRenderResult>(DocumentOcrErrors.OcrPdfRenderFailed));
        }
    }

    public Task<Result<int>> GetPageCountAsync(byte[] pdfBytes, CancellationToken cancellationToken)
    {
        if (pdfBytes.Length == 0)
            return Task.FromResult(Result.Failure<int>(DocumentOcrErrors.OcrPdfRenderFailed));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageCount = Conversion.GetPageCount(pdfBytes, null);
            return Task.FromResult(Result.Success(pageCount));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Task.FromResult(Result.Failure<int>(DocumentOcrErrors.OcrPdfRenderFailed));
        }
    }
}
