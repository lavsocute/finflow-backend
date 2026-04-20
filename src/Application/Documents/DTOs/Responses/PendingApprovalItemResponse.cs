namespace FinFlow.Application.Documents.DTOs.Responses;

public sealed record PendingApprovalItemResponse(
    Guid DocumentId,
    string Title,
    string Requester,
    string Department,
    decimal Amount,
    DateOnly DueDate,
    string Priority,
    string Status
);
