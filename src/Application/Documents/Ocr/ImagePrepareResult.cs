namespace FinFlow.Application.Documents.Ocr;

public sealed record ImagePrepareResult(
    IReadOnlyList<OcrPageImage> Pages,
    bool WasTruncated)
{
    public static ImagePrepareResult Success(IReadOnlyList<OcrPageImage> pages, bool wasTruncated = false)
        => new(pages, wasTruncated);
}