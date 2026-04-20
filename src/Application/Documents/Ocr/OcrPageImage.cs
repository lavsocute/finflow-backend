namespace FinFlow.Application.Documents.Ocr;

public sealed record OcrPageImage(
    int PageNumber,
    string ContentType,
    string Base64Content
);
