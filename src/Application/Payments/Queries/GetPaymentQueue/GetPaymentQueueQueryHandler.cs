using FinFlow.Application.Payments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.TenantMemberships;
using MediatR;

namespace FinFlow.Application.Payments.Queries.GetPaymentQueue;

public sealed class GetPaymentQueueQueryHandler
    : IRequestHandler<GetPaymentQueueQuery, Result<IReadOnlyList<PaymentQueueItemResponse>>>
{
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public GetPaymentQueueQueryHandler(
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

    public async Task<Result<IReadOnlyList<PaymentQueueItemResponse>>> Handle(GetPaymentQueueQuery request, CancellationToken cancellationToken)
    {
        var approvedDocuments = await _documentRepository.GetByStatusAsync(
            request.TenantId,
            ApprovalStatusFilter.Approved,
            search: null,
            page: 1,
            pageSize: int.MaxValue,
            cancellationToken);

        var payments = await _paymentRepository.GetByTenantIdAsync(request.TenantId, status: null, cancellationToken);
        var paymentByDocumentId = payments
            .GroupBy(x => x.DocumentId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(p => p.RecordedAt).First());

        var membershipMap = await LoadMembershipMapAsync(approvedDocuments.Select(x => x.MembershipId), cancellationToken);
        var accountMap = await LoadAccountMapAsync(membershipMap.Values.Select(x => x.AccountId), cancellationToken);
        var departmentMap = await LoadDepartmentMapAsync(approvedDocuments.Select(x => x.IdDepartment), cancellationToken);

        var items = approvedDocuments
            .Select(document =>
            {
                paymentByDocumentId.TryGetValue(document.Id, out var payment);
                var queueStatus = MapQueueStatus(payment);

                membershipMap.TryGetValue(document.MembershipId, out var membership);
                accountMap.TryGetValue(membership?.AccountId ?? Guid.Empty, out var account);
                departmentMap.TryGetValue(document.IdDepartment, out var departmentName);

                return new PaymentQueueItemResponse(
                    payment?.Id,
                    document.Id,
                    document.Reference,
                    document.OriginalFileName,
                    ResolveEmployeeName(account),
                    document.MembershipId.ToString(),
                    EmployeeCode: null,
                    MerchantName: document.VendorName,
                    Department: departmentName ?? string.Empty,
                    Amount: document.TotalAmount,
                    CurrencyCode: document.CurrencyCode,
                    AmountInBaseCurrency: payment?.AmountInBaseCurrency ?? document.TotalAmount,
                    ExpenseDate: document.DocumentDate,
                    SubmittedAt: document.SubmittedAt,
                    QueueStatus: queueStatus,
                    PaymentMethod: queueStatus == PaymentQueueStatuses.ReadyToPay ? null : payment?.Method.ToString(),
                    RecordedAt: payment?.RecordedAt,
                    ConfirmedAt: payment?.ConfirmedAt,
                    RejectionReason: payment?.RejectionReason,
                    Notes: payment?.Notes);
            })
            .Where(item => MatchesStatus(item, request.Status))
            .Where(item => MatchesSearch(item, request.Search))
            .OrderByDescending(item => item.SubmittedAt)
            .ToList();

        return Result.Success<IReadOnlyList<PaymentQueueItemResponse>>(items);
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

    private async Task<Dictionary<Guid, string>> LoadDepartmentMapAsync(
        IEnumerable<Guid> departmentIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, string>();

        foreach (var departmentId in departmentIds.Distinct().Where(x => x != Guid.Empty))
        {
            var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            if (department is not null)
            {
                map[departmentId] = department.Name;
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

    private static string MapQueueStatus(PaymentSummary? payment) =>
        payment?.Status switch
        {
            null => PaymentQueueStatuses.ReadyToPay,
            PaymentStatus.Pending => PaymentQueueStatuses.Scheduled,
            PaymentStatus.Confirmed => PaymentQueueStatuses.Paid,
            PaymentStatus.Rejected => PaymentQueueStatuses.Failed,
            _ => PaymentQueueStatuses.ReadyToPay
        };

    private static bool MatchesStatus(PaymentQueueItemResponse item, string? requestedStatus)
    {
        if (string.IsNullOrWhiteSpace(requestedStatus) ||
            requestedStatus.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return item.QueueStatus.Equals(requestedStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSearch(PaymentQueueItemResponse item, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var normalized = search.Trim().ToLowerInvariant();

        return item.Reference.ToLowerInvariant().Contains(normalized)
            || item.DocumentFileName.ToLowerInvariant().Contains(normalized)
            || item.EmployeeName.ToLowerInvariant().Contains(normalized)
            || item.EmployeeMembershipId.ToLowerInvariant().Contains(normalized)
            || (item.MerchantName?.ToLowerInvariant().Contains(normalized) ?? false)
            || item.Department.ToLowerInvariant().Contains(normalized);
    }

    private static class PaymentQueueStatuses
    {
        public const string ReadyToPay = "ReadyToPay";
        public const string Scheduled = "Scheduled";
        public const string Paid = "Paid";
        public const string Failed = "Failed";
    }
}
