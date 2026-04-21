using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;

namespace FinFlow.Infrastructure.Ocr.LlmVision;

public static class LlmVisionImagePreparer
{
    public static async Task<Result<ImagePrepareResult>> PrepareAsync(
        string contentType,
        byte[] fileContents,
        int maxImageBytes,
        int maxPagesPerDocument,
        int maxImagesPerRequest,
        IPdfPageRenderer pdfPageRenderer,
        CancellationToken cancellationToken)
    {
        if (fileContents.Length == 0)
            return Result.Failure<ImagePrepareResult>(DocumentOcrErrors.OcrFileEmpty);

        if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            var allowedPages = Math.Min(maxPagesPerDocument, maxImagesPerRequest);
            var renderResult = await pdfPageRenderer.RenderAsync(fileContents, allowedPages, cancellationToken);
            if (renderResult.IsFailure)
                return Result.Failure<ImagePrepareResult>(renderResult.Error);

            var renderedPages = renderResult.Value.Pages;
            var wasTruncated = renderResult.Value.WasTruncated;
            if (renderedPages.Count == 0)
                return Result.Failure<ImagePrepareResult>(DocumentOcrErrors.OcrPdfRenderFailed);
            if (renderedPages.Count > maxImagesPerRequest)
                return Result.Failure<ImagePrepareResult>(DocumentOcrErrors.OcrExtractionFailed);

            foreach (var page in renderedPages)
            {
                if (GetDecodedByteLength(page.Base64Content) > maxImageBytes)
                    return Result.Failure<ImagePrepareResult>(DocumentOcrErrors.OcrFileTooLarge);
            }

            return Result.Success(ImagePrepareResult.Success(renderedPages, wasTruncated));
        }

        if (!IsSupportedImage(contentType))
            return Result.Failure<ImagePrepareResult>(DocumentOcrErrors.OcrUnsupportedFile);

        if (fileContents.Length > maxImageBytes)
            return Result.Failure<ImagePrepareResult>(DocumentOcrErrors.OcrFileTooLarge);

        return Result.Success(ImagePrepareResult.Success(
        [
            new OcrPageImage(1, contentType, Convert.ToBase64String(fileContents))
        ]));
    }

    private static bool IsSupportedImage(string contentType) =>
        string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase);

    private static int GetDecodedByteLength(string base64Content) =>
        Convert.FromBase64String(base64Content).Length;
}
