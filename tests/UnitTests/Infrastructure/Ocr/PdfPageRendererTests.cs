using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr.Pdf;

namespace FinFlow.UnitTests.Infrastructure.Ocr;

public sealed class PdfPageRendererTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "Fixtures",
        "sample-single-page.pdf");
    private static readonly string MultiPageFixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "Fixtures",
        "sample-multi-page.pdf");

    [Fact]
    public async Task RenderAsync_ReturnsFirstPageAsPngImage()
    {
        var renderer = new PdfPageRenderer();
        var pdfBytes = await File.ReadAllBytesAsync(FixturePath);

        var result = await renderer.RenderAsync(pdfBytes, 1, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        var page = Assert.Single(result.Value.Pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("image/png", page.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(page.Base64Content));
    }

    [Fact]
    public async Task RenderAsync_ReturnsPdfRenderFailure_ForInvalidPdf()
    {
        var renderer = new PdfPageRenderer();

        var result = await renderer.RenderAsync([1, 2, 3], 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrPdfRenderFailed, result.Error);
    }

    [Fact]
    public async Task RenderAsync_ReturnsUpToRequestedPages_ForMultiPagePdf()
    {
        var renderer = new PdfPageRenderer();
        var pdfBytes = await File.ReadAllBytesAsync(MultiPageFixturePath);

        var result = await renderer.RenderAsync(pdfBytes, 3, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.True(result.Value.Pages.Count >= 2);
        Assert.True(result.Value.Pages.Count <= 3);
        Assert.Equal(Enumerable.Range(1, result.Value.Pages.Count), result.Value.Pages.Select(x => x.PageNumber));
        Assert.All(result.Value.Pages, page =>
        {
            Assert.Equal("image/png", page.ContentType);
            Assert.False(string.IsNullOrWhiteSpace(page.Base64Content));
        });
    }
}
