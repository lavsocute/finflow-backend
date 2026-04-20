namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record ReviewedDocumentResponse(
    Guid DocumentId,
    string Status,
    DateTime SubmittedAt,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    DateOnly DueDate,
    string ReviewedByStaff
);
