using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.TenantMemberships;
using MediatR;

namespace FinFlow.Application.Payments.Queries.GetPaymentDetail;

public sealed class GetPaymentDetailQueryHandler
    : IRequestHandler<GetPaymentDetailQuery, Result<PaymentDetailResponse?>>
{
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public GetPaymentDetailQueryHandler(
        IReviewedDocumentRepository documentRepository,
        IPaymentRepository paymentRepository,
        ITenantMembershipRepository membershipRepository,
        IAccountRepository accountRepository,
        IDepartmentRepository departmentRepository)
    {
        _documentRepository = documentRepository;
        _paymentRepository = paymentRepository;
        _membershipRepository = membershipRepository;
        _accountRepository = accountRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<Result<PaymentDetailResponse?>> Handle(GetPaymentDetailQuery request, CancellationToken cancellationToken)
    {
        if (!request.PaymentId.HasValue && !request.DocumentId.HasValue)
        {
            return Result.Failure<PaymentDetailResponse?>(
                new Error("Payment.DetailIdentifierRequired", "Either paymentId or documentId is required."));
        }

        PaymentSummary? payment = null;
        Guid documentId;

        if (request.PaymentId.HasValue)
        {
            payment = await _paymentRepository.GetByIdAsync(request.PaymentId.Value, cancellationToken);
            if (payment is null)
                return Result.Failure<PaymentDetailResponse?>(PaymentErrors.NotFound);

            documentId = payment.DocumentId;
        }
        else
        {
            documentId = request.DocumentId!.Value;
            payment = await _paymentRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        }

        var document = await _documentRepository.GetByIdForUpdateAsync(documentId, request.TenantId, cancellationToken);
        if (document is null)
            return Result.Failure<PaymentDetailResponse?>(PaymentErrors.NotFound);

        var membershipIds = new HashSet<Guid> { document.MembershipId };
        if (payment is not null)
        {
            membershipIds.Add(payment.RecordedByMembershipId);
            if (payment.ConfirmedByMembershipId.HasValue)
            {
                membershipIds.Add(payment.ConfirmedByMembershipId.Value);
            }
        }

        var membershipMap = await LoadMembershipMapAsync(membershipIds, cancellationToken);
        var accountMap = await LoadAccountMapAsync(membershipMap.Values.Select(x => x.AccountId), cancellationToken);
        var department = document.IdDepartment != Guid.Empty
            ? await _departmentRepository.GetByIdAsync(document.IdDepartment, cancellationToken)
            : null;

        var employeeMembership = membershipMap.GetValueOrDefault(document.MembershipId);
        var employeeAccount = employeeMembership is not null
            ? accountMap.GetValueOrDefault(employeeMembership.AccountId)
            : null;

        var queueStatus = MapQueueStatus(payment);
        var auditTrail = BuildAuditTrail(document, payment, membershipMap, accountMap);

        return Result.Success<PaymentDetailResponse?>(new PaymentDetailResponse(
            PaymentId: payment?.Id,
            DocumentId: document.Id,
            Reference: document.Reference,
            SettlementRef: payment is null ? null : BuildSettlementRef(payment),
            ApprovalRecordId: BuildApprovalRecordId(document),
            EmployeeName: ResolveEmployeeName(employeeAccount),
            EmployeeMembershipId: document.MembershipId.ToString(),
            EmployeeCode: null,
            MerchantName: document.VendorName,
            Department: department?.Name ?? string.Empty,
            CostCenter: null,
            Amount: document.TotalAmount,
            CurrencyCode: payment?.CurrencyCode.ToString() ?? "VND",
            AmountInVnd: payment?.AmountInVnd ?? document.TotalAmount,
            ExpenseDate: document.DocumentDate,
            PaymentMethod: queueStatus == PaymentQueueStatuses.ReadyToPay ? null : payment?.Method.ToString(),
            QueueStatus: queueStatus,
            DocumentFileName: document.OriginalFileName,
            DocumentDownloadUrl: null,
            DocumentViewUrl: null,
            AuditTrail: auditTrail,
            MethodSource: payment is null ? null : "SelectedByAccountant",
            MethodEditable: payment is null));
    }

    private async Task<Dictionary<Guid, TenantMembershipSummary>> LoadMembershipMapAsync(
        IEnumerable<Guid> membershipIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, TenantMembershipSummary>();

        foreach (var membershipId in membershipIds.Distinct())
        {
            var membership = await _membershipRepository.GetByIdAsync(membershipId, cancellationToken);
            if (membership is not null)
            {
                map[membershipId] = membership;
            }
        }

        return map;
    }

    private async Task<Dictionary<Guid, AccountSummary>> LoadAccountMapAsync(
        IEnumerable<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, AccountSummary>();

        foreach (var accountId in accountIds.Distinct().Where(x => x != Guid.Empty))
        {
            var account = await _accountRepository.GetByIdAsync(accountId, cancellationToken);
            if (account is not null)
            {
                map[accountId] = account;
            }
        }

        return map;
    }

    private static string ResolveEmployeeName(AccountSummary? account)
    {
        if (account is null)
            return "Unknown employee";

        if (!string.IsNullOrWhiteSpace(account.FullName))
            return account.FullName;

        return account.Email.Split('@')[0];
    }

    private static string ResolveActorName(
        Guid? membershipId,
        IReadOnlyDictionary<Guid, TenantMembershipSummary> memberships,
        IReadOnlyDictionary<Guid, AccountSummary> accounts,
        string fallback)
    {
        if (!membershipId.HasValue)
            return fallback;

        if (!memberships.TryGetValue(membershipId.Value, out var membership))
            return fallback;

        if (!accounts.TryGetValue(membership.AccountId, out var account))
            return fallback;

        return ResolveEmployeeName(account);
    }

    private static IReadOnlyList<PaymentAuditTrailItemResponse> BuildAuditTrail(
        ReviewedDocument document,
        PaymentSummary? payment,
        IReadOnlyDictionary<Guid, TenantMembershipSummary> memberships,
        IReadOnlyDictionary<Guid, AccountSummary> accounts)
    {
        var events = new List<PaymentAuditTrailItemResponse>
        {
            new(
                Type: "Approval",
                Title: "Expense approved for reimbursement",
                Actor: "System",
                Timestamp: document.UpdatedAt,
                Note: "Approved expense is now ready for accountant processing."),
            new(
                Type: "Queue",
                Title: "Added to reimbursement queue",
                Actor: "System",
                Timestamp: document.UpdatedAt,
                Note: "Queued after approval.")
        };

        if (payment is null)
        {
            events.Add(new PaymentAuditTrailItemResponse(
                Type: "Pending",
                Title: "Awaiting reimbursement scheduling",
                Actor: string.Empty,
                Timestamp: document.UpdatedAt,
                Note: null));

            return events;
        }

        events.Add(new PaymentAuditTrailItemResponse(
            Type: "Scheduled",
            Title: "Reimbursement scheduled",
            Actor: ResolveActorName(payment.RecordedByMembershipId, memberships, accounts, "Accountant"),
            Timestamp: payment.RecordedAt,
            Note: payment.Notes));

        if (payment.Status == PaymentStatus.Confirmed && payment.ConfirmedAt.HasValue)
        {
            events.Add(new PaymentAuditTrailItemResponse(
                Type: "Paid",
                Title: "Reimbursement confirmed",
                Actor: ResolveActorName(payment.ConfirmedByMembershipId, memberships, accounts, "System"),
                Timestamp: payment.ConfirmedAt.Value,
                Note: null));
        }
        else if (payment.Status == PaymentStatus.Rejected)
        {
            events.Add(new PaymentAuditTrailItemResponse(
                Type: "Failed",
                Title: "Reimbursement failed",
                Actor: ResolveActorName(payment.ConfirmedByMembershipId, memberships, accounts, "Accountant"),
                Timestamp: payment.ConfirmedAt ?? payment.RecordedAt,
                Note: payment.RejectionReason));
        }

        return events;
    }

    private static string BuildSettlementRef(PaymentSummary payment) =>
        $"SET-{payment.CreatedAt.Year}-{payment.Id.ToString()[..8].ToUpperInvariant()}";

    private static string BuildApprovalRecordId(ReviewedDocument document) =>
        $"APR-{document.CreatedAt.Year}-{document.Id.ToString()[..8].ToUpperInvariant()}";

    private static string MapQueueStatus(PaymentSummary? payment) =>
        payment?.Status switch
        {
            null => PaymentQueueStatuses.ReadyToPay,
            PaymentStatus.Pending => PaymentQueueStatuses.Scheduled,
            PaymentStatus.Confirmed => PaymentQueueStatuses.Paid,
            PaymentStatus.Rejected => PaymentQueueStatuses.Failed,
            _ => PaymentQueueStatuses.ReadyToPay
        };

    private static class PaymentQueueStatuses
    {
        public const string ReadyToPay = "ReadyToPay";
        public const string Scheduled = "Scheduled";
        public const string Paid = "Paid";
        public const string Failed = "Failed";
    }
}
