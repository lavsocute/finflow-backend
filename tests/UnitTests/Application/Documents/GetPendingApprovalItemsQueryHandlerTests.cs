using FinFlow.Application.Documents.Queries.GetPendingApprovalItems;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class GetPendingApprovalItemsQueryHandlerTests
{
    [Fact]
    public async Task Handle_UsesDocumentDateAsExpenseDate()
    {
        var tenantId = Guid.NewGuid();
        var documentDate = new DateOnly(2026, 4, 18);
        var lineItem = ReviewedDocumentLineItem.Create("Cloud Compute Instance", 1m, 850m, 850m);
        var document = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "invoice.pdf",
            "application/pdf",
            "Acme Cloud Ltd.",
            "INV-2026-0042",
            documentDate,
            "Software & SaaS",
            "TX-123",
            850m,
            0m,
            850m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.UtcNow,
            [lineItem]).Value;

        var repository = new StubReviewedDocumentRepository([document]);
        var handler = new GetPendingApprovalItemsQueryHandler(repository);

        var result = await handler.Handle(new GetPendingApprovalItemsQuery(tenantId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal(documentDate, item.ExpenseDate);
    }

    private sealed class StubReviewedDocumentRepository(IReadOnlyList<ReviewedDocument> documents) : IReviewedDocumentRepository
    {
        public void Add(ReviewedDocument document) => throw new NotSupportedException();
        public void Update(ReviewedDocument document) => throw new NotSupportedException();
        public Task<ReviewedDocument?> GetByIdForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReviewedDocument?> GetOwnedByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetAllActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetReadyForApprovalAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(documents);
        public Task<IReadOnlyList<ReviewedDocument>> GetOwnedSubmittedAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReviewedDocument>> GetByStatusAsync(Guid tenantId, FinFlow.Domain.Enums.ApprovalStatusFilter status, string? search, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountByStatusAsync(Guid tenantId, FinFlow.Domain.Enums.ApprovalStatusFilter status, string? search, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
