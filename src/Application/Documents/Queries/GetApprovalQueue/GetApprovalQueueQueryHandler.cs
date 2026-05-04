using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetApprovalQueue;

public sealed class GetApprovalQueueQueryHandler
    : IRequestHandler<GetApprovalQueueQuery, Result<ApprovalQueueResponse>>
{
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public GetApprovalQueueQueryHandler(
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

    public async Task<Result<ApprovalQueueResponse>> Handle(GetApprovalQueueQuery request, CancellationToken cancellationToken)
    {
        var documents = await _documentRepository.GetByStatusAsync(
            request.TenantId,
            request.Status,
            request.Search,
            request.Page,
            request.PageSize,
            cancellationToken);

        var totalCount = await _documentRepository.CountByStatusAsync(
            request.TenantId,
            request.Status,
            request.Search,
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

        var items = documents.Select(doc =>
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

            return new ApprovalQueueItemResponse(
                doc.Id,
                $"{doc.VendorName} · {doc.Reference}",
                doc.VendorName,
                account.FullName ?? account.Email.Split('@')[0],
                account.Email,
                departmentName ?? string.Empty,
                doc.TotalAmount,
                "VND",
                doc.DueDate,
                doc.SubmittedAt,
                doc.TotalAmount >= 5_000_000m ? "High" : "Medium",
                doc.Status.ToString(),
                policySummary);
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return Result.Success(new ApprovalQueueResponse(
            items,
            request.Page,
            request.PageSize,
            totalCount,
            totalPages));
    }
}