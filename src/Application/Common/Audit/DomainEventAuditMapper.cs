using System.Text.Json;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Events;

namespace FinFlow.Application.Common.Audit;

internal sealed class DomainEventAuditMapper : IDomainEventAuditMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AuditLog? Map(IDomainEvent domainEvent, Guid? tenantId, Guid? accountId)
    {
        return domainEvent switch
        {
            ExpenseRejectedDomainEvent e => CreateLog(
                "EXPENSE_REJECTED", "Expense", e.ExpenseId, tenantId, accountId,
                payload: new { status = "Rejected", reason = e.Reason, rejectedByMembershipId = e.RejectedByMembershipId }),

            ExpenseReopenedDomainEvent e => CreateLog(
                "EXPENSE_REOPENED", "Expense", e.ExpenseId, tenantId, accountId,
                payload: new { status = "Confirmed", reason = e.Reason, reopenedByMembershipId = e.ReopenedByMembershipId }),

            PaymentRecordedDomainEvent e => CreateLog(
                "PAYMENT_RECORDED", "Payment", e.PaymentId, tenantId, accountId,
                payload: new
                {
                    documentId = e.DocumentId,
                    amount = e.Amount,
                    currency = e.CurrencyCode,
                    method = e.Method.ToString(),
                    recordedByMembershipId = e.RecordedByMembershipId
                }),

            PaymentConfirmedDomainEvent e => CreateLog(
                "PAYMENT_CONFIRMED", "Payment", e.PaymentId, tenantId, accountId,
                payload: new
                {
                    status = "Confirmed",
                    amount = e.Amount,
                    currency = e.CurrencyCode,
                    executionReference = e.ExecutionReference,
                    confirmedByMembershipId = e.ConfirmedByMembershipId
                }),

            PaymentRejectedDomainEvent e => CreateLog(
                "PAYMENT_REJECTED", "Payment", e.PaymentId, tenantId, accountId,
                payload: new
                {
                    status = "Rejected",
                    rejectionType = e.RejectionType.ToString(),
                    reason = e.Reason,
                    rejectedByMembershipId = e.RejectedByMembershipId
                }),

            PaymentUpdatedDomainEvent e => CreateLog(
                "PAYMENT_UPDATED", "Payment", e.PaymentId, tenantId, accountId,
                oldPayload: new { method = e.OldMethod.ToString(), notes = e.OldNotes },
                payload: new
                {
                    method = e.NewMethod.ToString(),
                    notes = e.NewNotes,
                    updatedByMembershipId = e.UpdatedByMembershipId
                }),

            PaymentCancelledDomainEvent e => CreateLog(
                "PAYMENT_CANCELLED", "Payment", e.PaymentId, tenantId, accountId,
                payload: new
                {
                    status = "Cancelled",
                    reason = e.Reason,
                    cancelledByMembershipId = e.CancelledByMembershipId
                }),

            PaymentRefundedDomainEvent e => CreateLog(
                "PAYMENT_REFUNDED", "Payment", e.PaymentId, tenantId, accountId,
                payload: new
                {
                    refundAmount = e.RefundAmount,
                    reason = e.Reason,
                    initiatedByMembershipId = e.InitiatedByMembershipId
                }),

            BudgetCreatedDomainEvent e => CreateLog(
                "BUDGET_CREATED", "Budget", e.BudgetId, tenantId, accountId,
                payload: new
                {
                    departmentId = e.DepartmentId,
                    month = e.Month,
                    year = e.Year,
                    allocatedAmount = e.AllocatedAmount
                }),

            BudgetUpdatedDomainEvent e => CreateLog(
                "BUDGET_UPDATED", "Budget", e.BudgetId, tenantId, accountId,
                payload: new
                {
                    departmentId = e.DepartmentId,
                    allocatedAmount = e.AllocatedAmount,
                    spentAmount = e.SpentAmount
                }),

            ReviewedDocumentApprovedDomainEvent e => CreateLog(
                "DOCUMENT_APPROVED", "ReviewedDocument", e.DocumentId, tenantId, accountId,
                payload: new { status = "Approved", approvedByMembershipId = e.ApprovedByMembershipId }),

            ReviewedDocumentRejectedDomainEvent e => CreateLog(
                "DOCUMENT_REJECTED", "ReviewedDocument", e.DocumentId, tenantId, accountId,
                payload: new { status = "Rejected", reason = e.Reason, rejectedByMembershipId = e.RejectedByMembershipId }),

            ReviewedDocumentWithdrawnDomainEvent e => CreateLog(
                "DOCUMENT_WITHDRAWN", "ReviewedDocument", e.DocumentId, tenantId, accountId,
                payload: new { membershipId = e.MembershipId }),

            UploadedDocumentDraftDeletedDomainEvent e => CreateLog(
                "DOCUMENT_DRAFT_DELETED", "UploadedDocumentDraft", e.DraftId, tenantId, accountId,
                payload: new { membershipId = e.MembershipId }),

            _ => null
        };
    }

    private static AuditLog CreateLog(
        string action,
        string entityType,
        Guid entityId,
        Guid? tenantId,
        Guid? accountId,
        object? payload = null,
        object? oldPayload = null)
    {
        return AuditLog.Create(
            action,
            entityType,
            entityId.ToString(),
            oldValue: oldPayload is null ? null : JsonSerializer.Serialize(oldPayload, JsonOptions),
            newValue: payload is null ? null : JsonSerializer.Serialize(payload, JsonOptions),
            ipAddress: null,
            userAgent: null,
            idTenant: tenantId,
            idAccount: accountId);
    }
}
