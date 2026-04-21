using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr.LlmVision;

namespace FinFlow.UnitTests.Infrastructure.Ocr;

public sealed class LlmVisionImagePreparerTests
{
    [Fact]
    public async Task PrepareAsync_ReturnsBase64Image_ForSupportedImage()
    {
        var result = await LlmVisionImagePreparer.PrepareAsync(
            "image/png",
            [1, 2, 3],
            maxImageBytes: 4,
            maxPagesPerDocument: 3,
            maxImagesPerRequest: 5,
            pdfPageRenderer: new StubPdfPageRenderer(Result.Success<PdfRenderResult>(new PdfRenderResult([], 0, false))),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Single(result.Value.Pages);
        Assert.Equal("image/png", result.Value.Pages[0].ContentType);
        Assert.Equal("AQID", result.Value.Pages[0].Base64Content);
    }

    [Fact]
    public async Task PrepareAsync_ReturnsUnsupportedFile_ForUnsupportedType()
    {
        var result = await LlmVisionImagePreparer.PrepareAsync(
            "text/plain",
            [1, 2, 3],
            maxImageBytes: 4,
            maxPagesPerDocument: 3,
            maxImagesPerRequest: 5,
            pdfPageRenderer: new StubPdfPageRenderer(Result.Success<PdfRenderResult>(new PdfRenderResult([], 0, false))),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrUnsupportedFile, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_ReturnsFileTooLarge_ForOversizedImage()
    {
        var result = await LlmVisionImagePreparer.PrepareAsync(
            "image/png",
            [1, 2, 3, 4, 5],
            maxImageBytes: 4,
            maxPagesPerDocument: 3,
            maxImagesPerRequest: 5,
            pdfPageRenderer: new StubPdfPageRenderer(Result.Success<PdfRenderResult>(new PdfRenderResult([], 0, false))),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrFileTooLarge, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_ReturnsPdfRenderFailure_ForPdfWhenRendererFails()
    {
        var result = await LlmVisionImagePreparer.PrepareAsync(
            "application/pdf",
            [1, 2, 3],
            maxImageBytes: 4,
            maxPagesPerDocument: 3,
            maxImagesPerRequest: 5,
            pdfPageRenderer: new StubPdfPageRenderer(Result.Failure<PdfRenderResult>(DocumentOcrErrors.OcrPdfRenderFailed)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrPdfRenderFailed, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_ReturnsRenderedPdfPage_WhenRendererSucceeds()
    {
        var renderedPage = new OcrPageImage(1, "image/png", "AQID");
        var renderer = new StubPdfPageRenderer(Result.Success<PdfRenderResult>(new PdfRenderResult([renderedPage], 3, false)));
        var result = await LlmVisionImagePreparer.PrepareAsync(
            "application/pdf",
            [1, 2, 3],
            maxImageBytes: 4,
            maxPagesPerDocument: 3,
            maxImagesPerRequest: 5,
            pdfPageRenderer: renderer,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        var page = Assert.Single(result.Value.Pages);
        Assert.Equal(renderedPage, page);
        Assert.Equal(3, renderer.LastMaxPages);
    }

    [Fact]
    public async Task PrepareAsync_UsesSmallerOfMaxPagesAndMaxImages()
    {
        var renderedPage = new OcrPageImage(1, "image/png", "AQID");
        var renderer = new StubPdfPageRenderer(Result.Success<PdfRenderResult>(new PdfRenderResult([renderedPage], 3, false)));
        var result = await LlmVisionImagePreparer.PrepareAsync(
            "application/pdf",
            [1, 2, 3],
            maxImageBytes: 4,
            maxPagesPerDocument: 3,
            maxImagesPerRequest: 2,
            pdfPageRenderer: renderer,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(2, renderer.LastMaxPages);
    }

    private sealed class StubPdfPageRenderer : IPdfPageRenderer
    {
        private readonly Result<PdfRenderResult> _result;

        public StubPdfPageRenderer(Result<PdfRenderResult> result) => _result = result;

        public int? LastMaxPages { get; private set; }

        public Task<Result<PdfRenderResult>> RenderAsync(byte[] pdfBytes, int maxPages, CancellationToken cancellationToken) =>
            Task.FromResult(Track(maxPages));

        private Result<PdfRenderResult> Track(int maxPages)
        {
            LastMaxPages = maxPages;
            return _result;
        }
    }
}
