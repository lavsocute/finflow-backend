using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Documents.Commands.ApproveReviewedDocument;

/// <summary>
/// Approve a reviewed document. <paramref name="OverrideJustification"/> is
/// supplied only when the manager has been told the budget guard requires an
/// override (mode=SoftBlock and amount would exceed). The handler validates
/// the justification length and emits <c>BudgetOverrideUsedDomainEvent</c> for
/// audit. Leaving null on a budget that requires override → command fails
/// with <c>Budget.OverrideRequired</c>.
/// </summary>
public sealed record ApproveReviewedDocumentCommand(
    Guid DocumentId,
    Guid TenantId,
    Guid MembershipId,
    string? Comment = null,
    string? OverrideJustification = null) : ICommand<Result<ReviewedDocumentResponse>>;
