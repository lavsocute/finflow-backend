using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class DocumentOcrErrors
{
    public static readonly Error OcrProviderUnavailable = new("Documents.OcrProviderUnavailable", "The configured OCR provider is unavailable.");
    public static readonly Error OcrExtractionFailed = new("Documents.OcrExtractionFailed", "The OCR service was unable to extract document data.");
    public static readonly Error OcrInvalidJson = new("Documents.OcrInvalidJson", "The OCR service returned invalid response data.");
    public static readonly Error OcrUnsupportedFile = new("Documents.OcrUnsupportedFile", "The selected file type is not supported by the OCR service.");
    public static readonly Error OcrPdfRenderFailed = new("Documents.OcrPdfRenderFailed", "The OCR service was unable to render the PDF for extraction.");
    public static readonly Error OcrFileTooLarge = new("Documents.OcrFileTooLarge", "The file is too large for OCR processing.");
}
