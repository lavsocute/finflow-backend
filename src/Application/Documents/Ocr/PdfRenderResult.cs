namespace FinFlow.Application.Documents.Ocr;

public sealed record PdfRenderResult(
    IReadOnlyList<OcrPageImage> Pages,
    int TotalPageCount,
    bool WasTruncated)
{
    public static PdfRenderResult Success(IReadOnlyList<OcrPageImage> pages, int totalPageCount)
        => new(pages, totalPageCount, pages.Count < totalPageCount);
}