using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;

namespace FinFlow.Infrastructure.Ocr.LlmVision;

public static class LlmVisionImagePreparer
{
    public static async Task<Result<IReadOnlyList<OcrPageImage>>> PrepareAsync(
        string contentType,
        byte[] fileContents,
        int maxImageBytes,
        int maxPagesPerDocument,
        int maxImagesPerRequest,
        IPdfPageRenderer pdfPageRenderer,
        CancellationToken cancellationToken)
    {
        if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            var allowedPages = Math.Min(maxPagesPerDocument, maxImagesPerRequest);
            var renderResult = await pdfPageRenderer.RenderAsync(fileContents, allowedPages, cancellationToken);
            if (renderResult.IsFailure)
                return Result.Failure<IReadOnlyList<OcrPageImage>>(renderResult.Error);

            var renderedPages = renderResult.Value;
            if (renderedPages.Count == 0)
                return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrPdfRenderFailed);
            if (renderedPages.Count > maxImagesPerRequest)
                return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrExtractionFailed);

            foreach (var page in renderedPages)
            {
                if (GetDecodedByteLength(page.Base64Content) > maxImageBytes)
                    return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrFileTooLarge);
            }

            return Result.Success<IReadOnlyList<OcrPageImage>>(renderedPages);
        }

        if (!IsSupportedImage(contentType))
            return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrUnsupportedFile);

        if (fileContents.Length > maxImageBytes)
            return Result.Failure<IReadOnlyList<OcrPageImage>>(DocumentOcrErrors.OcrFileTooLarge);

        return Result.Success<IReadOnlyList<OcrPageImage>>(
        [
            new OcrPageImage(1, contentType, Convert.ToBase64String(fileContents))
        ]);
    }

    private static bool IsSupportedImage(string contentType) =>
        string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase);

    private static int GetDecodedByteLength(string base64Content) =>
        Convert.FromBase64String(base64Content).Length;
}
