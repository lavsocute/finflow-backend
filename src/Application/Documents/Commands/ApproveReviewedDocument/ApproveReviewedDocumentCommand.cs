using FinFlow.Application.Common;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Documents.Commands.ApproveReviewedDocument;

/// <summary>
/// Approve a reviewed document. <paramref name="OverrideJustification"/> is
/// supplied only when the manager has been told the budget guard requires an
/// override (mode=SoftBlock and amount would exceed). The handler validates
/// the justification length and emits <c>BudgetOverrideUsedDomainEvent</c> for
/// audit. Leaving null on a budget that requires override → command fails
/// with <c>Budget.OverrideRequired</c>.
/// <para>
/// <paramref name="ApproverRole"/> is used by the escalation logic to decide
/// whether this approval is a first-level (Manager) or second-level
/// (Accountant/TenantAdmin) approval.
/// </para>
/// </summary>
public sealed record ApproveReviewedDocumentCommand(
    Guid DocumentId,
    Guid TenantId,
    Guid MembershipId,
    RoleType ApproverRole = RoleType.Manager,
    string? Comment = null,
    string? OverrideJustification = null) : ICommand<Result<ReviewedDocumentResponse>>;
