using System.Text.Json;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Notifications;
using FinFlow.Domain.TenantMemberships;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.Application.Common.Notifications;
internal sealed class DomainEventNotificationMapper : IDomainEventNotificationMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Lazy-resolved to break the circular DI: DbContext → mapper → repos → DbContext.
    // The mapper is instantiated by DbContext during SaveChangesAsync, but repos
    // depend on DbContext. We resolve them on first use, by which point the
    // DbContext is already constructed.
    private readonly IServiceProvider _serviceProvider;

    public DomainEventNotificationMapper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private IReviewedDocumentRepository DocumentRepo =>
        _serviceProvider.GetRequiredService<IReviewedDocumentRepository>();
    private IPaymentRepository PaymentRepo =>
        _serviceProvider.GetRequiredService<IPaymentRepository>();
    private ITenantMembershipRepository MembershipRepo =>
        _serviceProvider.GetRequiredService<ITenantMembershipRepository>();

    public async Task<IReadOnlyList<Notification>> MapAsync(
        IDomainEvent domainEvent,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            return [];

        return domainEvent switch
        {
            ReviewedDocumentRejectedDomainEvent e => await BuildDocRejected(e, tenantId.Value, cancellationToken),
            ReviewedDocumentApprovedDomainEvent e => await BuildDocApproved(e, tenantId.Value, cancellationToken),
            PaymentRejectedDomainEvent e => await BuildPaymentRejected(e, tenantId.Value, cancellationToken),
            PaymentConfirmedDomainEvent e => await BuildPaymentConfirmed(e, tenantId.Value, cancellationToken),
            ExpenseRejectedDomainEvent e => await BuildExpenseRejected(e, tenantId.Value, cancellationToken),
            VendorAutoCreatedDomainEvent e => await BuildVendorAutoCreated(e, tenantId.Value, cancellationToken),
            DuplicateReceiptFlaggedDomainEvent e => await BuildDuplicateFlagged(e, tenantId.Value, cancellationToken),
            _ => []
        };
    }

    private async Task<IReadOnlyList<Notification>> BuildDocRejected(
        ReviewedDocumentRejectedDomainEvent e, Guid tenantId, CancellationToken ct)
    {
        var doc = await ResolveDocSubmitter(e.DocumentId, tenantId, ct);
        if (doc is null) return [];
        return Single(Notification.Create(
            tenantId, doc.SubmitterMembershipId,
            type: "DOCUMENT_REJECTED",
            title: "Hóa đơn đã bị từ chối",
            body: $"Hóa đơn {doc.Reference} bị từ chối: {Truncate(e.Reason, 200)}",
            payloadJson: SerializePayload(new { documentId = e.DocumentId, reason = e.Reason }),
            severity: NotificationSeverity.Warning));
    }

    private async Task<IReadOnlyList<Notification>> BuildDocApproved(
        ReviewedDocumentApprovedDomainEvent e, Guid tenantId, CancellationToken ct)
    {
        var doc = await ResolveDocSubmitter(e.DocumentId, tenantId, ct);
        if (doc is null) return [];
        return Single(Notification.Create(
            tenantId, doc.SubmitterMembershipId,
            type: "DOCUMENT_APPROVED",
            title: "Hóa đơn đã được duyệt",
            body: $"Hóa đơn {doc.Reference} đã được duyệt và chờ chi trả.",
            payloadJson: SerializePayload(new { documentId = e.DocumentId }),
            severity: NotificationSeverity.Info));
    }

    private async Task<IReadOnlyList<Notification>> BuildPaymentRejected(
        PaymentRejectedDomainEvent e, Guid tenantId, CancellationToken ct)
    {
        var payment = await PaymentRepo.GetByIdAsync(e.PaymentId, ct);
        if (payment is null) return [];
        var doc = await ResolveDocSubmitter(payment.DocumentId, tenantId, ct);
        if (doc is null) return [];
        return Single(Notification.Create(
            tenantId, doc.SubmitterMembershipId,
            type: "PAYMENT_REJECTED",
            title: "Yêu cầu hoàn tiền bị từ chối",
            body: $"Hoàn tiền cho hóa đơn {doc.Reference} bị từ chối ({e.RejectionType}). {Truncate(e.Reason, 150)}",
            payloadJson: SerializePayload(new { paymentId = e.PaymentId, documentId = payment.DocumentId, rejectionType = e.RejectionType.ToString() }),
            severity: NotificationSeverity.Critical));
    }

    private async Task<IReadOnlyList<Notification>> BuildPaymentConfirmed(
        PaymentConfirmedDomainEvent e, Guid tenantId, CancellationToken ct)
    {
        var payment = await PaymentRepo.GetByIdAsync(e.PaymentId, ct);
        if (payment is null) return [];
        var doc = await ResolveDocSubmitter(payment.DocumentId, tenantId, ct);
        if (doc is null) return [];
        return Single(Notification.Create(
            tenantId, doc.SubmitterMembershipId,
            type: "PAYMENT_CONFIRMED",
            title: "Đã được hoàn tiền",
            body: $"Hóa đơn {doc.Reference} đã được hoàn {e.Amount:N0} {e.CurrencyCode}.",
            payloadJson: SerializePayload(new { paymentId = e.PaymentId, amount = e.Amount, currency = e.CurrencyCode }),
            severity: NotificationSeverity.Info));
    }

    private async Task<IReadOnlyList<Notification>> BuildExpenseRejected(
        ExpenseRejectedDomainEvent e, Guid tenantId, CancellationToken ct)
    {
        // Expense events don't carry the submitter membership directly. We
        // notify the submitter membership stored on the linked document only
        // when easy to resolve; otherwise broadcast is too costly so we skip.
        // For now we don't have a clean lookup, so return empty.
        await Task.CompletedTask;
        return [];
    }

    private async Task<IReadOnlyList<Notification>> BuildVendorAutoCreated(
        VendorAutoCreatedDomainEvent e, Guid tenantId, CancellationToken ct)
    {
        var recipients = await ResolveTenantAdmins(tenantId, ct);
        if (recipients.Count == 0) return [];

        return recipients
            .Select(membershipId => Notification.Create(
                tenantId, membershipId,
                type: "VENDOR_AUTO_CREATED",
                title: "Vendor mới chờ duyệt",
                body: $"Hệ thống tự tạo vendor '{Truncate(e.Name, 100)}' (mã thuế {e.TaxCode}). Vui lòng kiểm tra và verify.",
                payloadJson: SerializePayload(new { vendorId = e.VendorId, taxCode = e.TaxCode, sourceDocumentId = e.SourceDocumentId }),
                severity: NotificationSeverity.Info))
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .ToList();
    }

    private async Task<IReadOnlyList<Notification>> BuildDuplicateFlagged(
        DuplicateReceiptFlaggedDomainEvent e, Guid tenantId, CancellationToken ct)
    {
        // Notify accountants + tenant admins. Conflict info goes in payload.
        var recipients = await ResolveAccountantsAndAdmins(tenantId, ct);
        if (recipients.Count == 0) return [];

        return recipients
            .Select(membershipId => Notification.Create(
                tenantId, membershipId,
                type: "DUPLICATE_RECEIPT_FLAGGED",
                title: "Phát hiện hóa đơn nghi ngờ trùng lặp",
                body: $"Hóa đơn vừa nộp có {e.ConflictingDocumentIds.Count} bản trùng trong 90 ngày qua. Vui lòng kiểm tra.",
                payloadJson: SerializePayload(new
                {
                    documentId = e.DocumentId,
                    submitterMembershipId = e.SubmitterMembershipId,
                    dedupHash = e.DedupHash,
                    conflictingDocumentIds = e.ConflictingDocumentIds
                }),
                severity: NotificationSeverity.Warning))
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .ToList();
    }

    private record DocSnapshot(Guid SubmitterMembershipId, string Reference);

    private async Task<DocSnapshot?> ResolveDocSubmitter(Guid documentId, Guid tenantId, CancellationToken ct)
    {
        var docs = await DocumentRepo.GetByIdsAsync([documentId], tenantId, ct);
        var doc = docs.FirstOrDefault();
        return doc is null ? null : new DocSnapshot(doc.MembershipId, doc.Reference);
    }

    private async Task<IReadOnlyList<Guid>> ResolveTenantAdmins(Guid tenantId, CancellationToken ct)
    {
        var memberships = await MembershipRepo.GetByTenantIdAsync(tenantId, ct);
        return memberships
            .Where(m => m.IsActive && m.Role == RoleType.TenantAdmin)
            .Select(m => m.Id)
            .ToList();
    }

    private async Task<IReadOnlyList<Guid>> ResolveAccountantsAndAdmins(Guid tenantId, CancellationToken ct)
    {
        var memberships = await MembershipRepo.GetByTenantIdAsync(tenantId, ct);
        return memberships
            .Where(m => m.IsActive && (m.Role == RoleType.Accountant || m.Role == RoleType.TenantAdmin))
            .Select(m => m.Id)
            .ToList();
    }

    private static IReadOnlyList<Notification> Single(Result<Notification> result) =>
        result.IsSuccess ? [result.Value] : [];

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }

    private static string SerializePayload(object payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);
}
