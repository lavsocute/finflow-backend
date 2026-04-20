namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record MySubmittedDocumentSummaryResponse(
    Guid DocumentId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    string Status,
    string SubmittedByEmail,
    DateTime SubmittedAt,
    DateTime LastUpdatedAt,
    string? RejectionReason
);
