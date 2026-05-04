using System.Text;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using MediatR;

namespace FinFlow.Application.Documents.Queries.ExportApprovalQueue;

public sealed class ExportApprovalQueueQueryHandler
    : IRequestHandler<ExportApprovalQueueQuery, Result<ExportApprovalQueueResponse>>
{
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public ExportApprovalQueueQueryHandler(
        IReviewedDocumentRepository documentRepository,
        ITenantMembershipRepository membershipRepository,
        IAccountRepository accountRepository,
        IDepartmentRepository departmentRepository)
    {
        _documentRepository = documentRepository;
        _membershipRepository = membershipRepository;
        _accountRepository = accountRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<Result<ExportApprovalQueueResponse>> Handle(ExportApprovalQueueQuery request, CancellationToken cancellationToken)
    {
        var documents = await _documentRepository.GetByStatusAsync(
            request.TenantId,
            request.Status,
            request.Search,
            page: 1,
            pageSize: 10000,
            cancellationToken);

        var membershipIds = documents.Select(x => x.MembershipId).Distinct().ToList();
        var memberships = new Dictionary<Guid, (Guid AccountId, Guid? DepartmentId)>();
        foreach (var membershipId in membershipIds)
        {
            var m = await _membershipRepository.GetByIdAsync(membershipId, cancellationToken);
            if (m is not null)
            {
                memberships[membershipId] = (m.AccountId, m.DepartmentId);
            }
        }

        var accountIds = memberships.Values.Select(x => x.AccountId).Distinct().ToList();
        var accounts = new Dictionary<Guid, (string Email, string? FullName)>();
        foreach (var accountId in accountIds)
        {
            var a = await _accountRepository.GetByIdAsync(accountId, cancellationToken);
            if (a is not null)
            {
                accounts[accountId] = (a.Email, a.FullName);
            }
        }

        var departmentIds = memberships.Values
            .Where(x => x.DepartmentId.HasValue)
            .Select(x => x.DepartmentId!.Value)
            .Distinct()
            .ToList();
        var departments = new Dictionary<Guid, string>();
        foreach (var deptId in departmentIds)
        {
            var d = await _departmentRepository.GetByIdAsync(deptId, cancellationToken);
            if (d is not null)
            {
                departments[deptId] = d.Name;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("DocumentId,Title,VendorName,Requester,RequesterEmail,Department,Amount,Currency,DueDate,SubmittedAt,Priority,Status,PolicySummary");

        foreach (var doc in documents)
        {
            var membership = memberships.GetValueOrDefault(doc.MembershipId);
            var account = accounts.GetValueOrDefault(membership.AccountId);
            var departmentName = membership.DepartmentId.HasValue
                ? departments.GetValueOrDefault(membership.DepartmentId.Value)
                : null;

            var policySummary = doc.TotalAmount switch
            {
                >= 10_000_000m => "Requires TenantAdmin approval",
                >= 5_000_000m => "Requires Manager approval",
                _ => "Auto-approved"
            };

            var priority = doc.TotalAmount >= 5_000_000m ? "High" : "Medium";

            sb.AppendLine($"{doc.Id},{Escape(doc.VendorName + " · " + doc.Reference)},{Escape(doc.VendorName)},{Escape(account.FullName ?? account.Email.Split('@')[0])},{Escape(account.Email)},{Escape(departmentName ?? "")},{doc.TotalAmount},VND,{doc.DueDate:yyyy-MM-dd},{doc.SubmittedAt:yyyy-MM-dd HH:mm},{priority},{doc.Status},{Escape(policySummary)}");
        }

        var csvContent = sb.ToString();
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(csvContent));
        var downloadUrl = $"data:text/csv;base64,{base64}";

        var statusLabel = request.Status switch
        {
            Domain.Enums.ApprovalStatusFilter.Pending => "pending",
            Domain.Enums.ApprovalStatusFilter.Approved => "approved",
            Domain.Enums.ApprovalStatusFilter.Rejected => "rejected",
            _ => "all"
        };
        var fileName = $"approval-queue-{statusLabel}-{DateTime.UtcNow:yyyyMMdd}.csv";

        return Result.Success(new ExportApprovalQueueResponse(fileName, downloadUrl));
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}