using FinFlow.Application.Documents.Queries.GetApprovalDetail;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantMemberships;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class GetApprovalDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsInvoiceBreakdown_WithTaxLines()
    {
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var document = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            tenantId,
            departmentId,
            membershipId,
            "receipt.pdf",
            "application/pdf",
            "Co.op Mart",
            "RCPT-001",
            new DateOnly(2026, 5, 27),
            "Groceries",
            null,
            300000m,
            25000m,
            325000m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.UtcNow,
            [
                ReviewedDocumentLineItem.Create("Fresh food", 1m, 100000m, null, 0m, 100000m, 5m, 100000m, 5000m),
                ReviewedDocumentLineItem.Create("Household item", 1m, 200000m, null, 0m, 200000m, 10m, 200000m, 20000m)
            ],
            [
                ReviewedDocumentTaxLine.Create("VAT", 5m, 100000m, 5000m).Value,
                ReviewedDocumentTaxLine.Create("VAT", 10m, 200000m, 20000m).Value
            ]).Value;

        var handler = new GetApprovalDetailQueryHandler(
            new StubReviewedDocumentRepository(document),
            new StubMembershipRepository(new TenantMembershipSummary(membershipId, accountId, tenantId, departmentId, RoleType.Staff, false, true, DateTime.UtcNow, null, null, null)),
            new StubAccountRepository(new AccountSummary(accountId, "staff@finflow.test", "Staff User", true, true, DateTime.UtcNow)),
            new StubDepartmentRepository(new DepartmentSummary(departmentId, "Operations", tenantId, null, true)));

        var result = await handler.Handle(new GetApprovalDetailQuery(tenantId, document.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(300000m, result.Value.Subtotal);
        Assert.Equal(25000m, result.Value.Vat);
        Assert.Equal(325000m, result.Value.TotalAmount);
        Assert.Equal(2, result.Value.LineItems.Count);
        Assert.Collection(
            result.Value.TaxLines.OrderBy(line => line.Rate).ToList(),
            line =>
            {
                Assert.Equal(5m, line.Rate);
                Assert.Equal(100000m, line.TaxableAmount);
                Assert.Equal(5000m, line.TaxAmount);
            },
            line =>
            {
                Assert.Equal(10m, line.Rate);
                Assert.Equal(200000m, line.TaxableAmount);
                Assert.Equal(20000m, line.TaxAmount);
            });
    }

    private sealed class StubReviewedDocumentRepository(ReviewedDocument document) : IReviewedDocumentRepository
    {
        public Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(document.Id == id && document.IdTenant == tenantId ? document : null);

        public Task<IReadOnlyList<ReviewedDocument>> GetByIdsAsync(IReadOnlyList<Guid> ids, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewedDocument?> GetOwnedByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetAllActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalByDepartmentAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetOwnedReadyForApprovalAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountByStatusAsync(Guid tenantId, ApprovalStatusFilter status, string? search, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(ReviewedDocument document) => throw new NotSupportedException();
        public void Update(ReviewedDocument document) => throw new NotSupportedException();
    }

    private sealed class StubMembershipRepository(TenantMembershipSummary membership) : ITenantMembershipRepository
    {
        public Task<TenantMembershipSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == membership.Id ? membership : null);

        public Task<IReadOnlyList<TenantMembershipSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantMembershipSummary?> GetActiveByAccountAndTenantAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetActiveByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantMembershipSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid accountId, Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsOwnerByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasActiveMembersInDepartmentAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantMembership?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(TenantMembership membership) => throw new NotSupportedException();
        public void Update(TenantMembership membership) => throw new NotSupportedException();
    }

    private sealed class StubAccountRepository(AccountSummary account) : IAccountRepository
    {
        public Task<AccountSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == account.Id ? account : null);

        public Task<IReadOnlyList<AccountSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountLoginInfo?> GetLoginInfoByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByEmailIgnoringTenantAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Account account) => throw new NotSupportedException();
        public void Update(Account account) => throw new NotSupportedException();
        public void Remove(Account account) => throw new NotSupportedException();
    }

    private sealed class StubDepartmentRepository(DepartmentSummary department) : IDepartmentRepository
    {
        public Task<DepartmentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == department.Id ? department : null);

        public Task<IReadOnlyList<DepartmentSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DepartmentSummary?> GetDefaultByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DepartmentSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Department?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<(Guid Id, Guid? ParentId)>> GetParentMapAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasActiveChildrenAsync(Guid parentId, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? excludeDepartmentId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Department?> GetEntityByIdIncludingInactiveAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Department department) => throw new NotSupportedException();
        public void Update(Department department) => throw new NotSupportedException();
        public void Remove(Department department) => throw new NotSupportedException();
    }
}
