using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.TenantMemberships;
using MediatR;

namespace FinFlow.Application.Documents.Queries.GetApprovalDetail;

public sealed class GetApprovalDetailQueryHandler
    : IRequestHandler<GetApprovalDetailQuery, Result<ApprovalDetailResponse>>
{
    private readonly IReviewedDocumentRepository _documentRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public GetApprovalDetailQueryHandler(
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

    public async Task<Result<ApprovalDetailResponse>> Handle(GetApprovalDetailQuery request, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdForUpdateAsync(request.DocumentId, request.TenantId, cancellationToken);
        if (document is null)
            return Result.Failure<ApprovalDetailResponse>(ReviewedDocumentErrors.NotFound);

        var membership = await _membershipRepository.GetByIdAsync(document.MembershipId, cancellationToken);
        var account = membership is not null ? await _accountRepository.GetByIdAsync(membership.AccountId, cancellationToken) : null;
        var department = document.IdDepartment != Guid.Empty
            ? await _departmentRepository.GetByIdAsync(document.IdDepartment, cancellationToken)
            : null;

        var requesterEmail = account?.Email ?? string.Empty;
        var departmentName = department?.Name ?? string.Empty;

        var lineItems = document.LineItems
            .Select(item => new ApprovalLineItemResponse(
                item.ItemName,
                item.Quantity,
                item.UnitPrice,
                item.DiscountPercent,
                item.DiscountAmount,
                item.TaxRate,
                item.TaxableAmount,
                item.TaxAmount,
                item.Total))
            .ToList();

        var policySummary = document.TotalAmount switch
        {
            >= 10_000_000m => "Requires TenantAdmin approval",
            >= 5_000_000m => "Requires Manager approval",
            _ => "Auto-approved"
        };

        var requestCode = $"DOC-{document.CreatedAt.Year}-{document.Id.ToString()[..8].ToUpperInvariant()}";

        return Result.Success(new ApprovalDetailResponse(
            document.Id,
            requestCode,
            $"{document.VendorName} · {document.Reference}",
            document.VendorName,
            account?.FullName ?? account?.Email.Split('@')[0] ?? string.Empty,
            requesterEmail,
            departmentName,
            document.TotalAmount,
            "VND",
            document.Subtotal,
            document.DocumentDiscountPercent,
            document.DocumentDiscountAmount,
            document.Vat,
            document.TotalAmount,
            document.DocumentDate,
            document.SubmittedAt,
            document.TotalAmount >= 5000m ? "High" : "Medium",
            document.Status.ToString(),
            policySummary,
            lineItems,
            document.TaxLines
                .Select(item => new DocumentTaxLineResponse(
                    item.TaxType,
                    item.Rate,
                    item.TaxableAmount,
                    item.TaxAmount))
                .ToList()));
    }
}
