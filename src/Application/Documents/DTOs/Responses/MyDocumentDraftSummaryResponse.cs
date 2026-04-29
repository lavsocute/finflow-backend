namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record MyDocumentDraftSummaryResponse(
    Guid DocumentId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    string Category,
    string Source,
    string ConfidenceLabel,
    string OwnerEmail,
    DateTime UploadedAt
);
