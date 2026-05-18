using FinFlow.Application.Budgets.Services;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.Commands.ApproveReviewedDocument;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSettings;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class ApproveReviewedDocumentCommandHandlerTests
{
    [Fact]
    public async Task Handle_IndexesReviewedDocument_AfterSuccessfulApproval()
    {
        var (sut, indexer, _) = BuildSut(out var doc);

        var result = await sut.Handle(
            new ApproveReviewedDocumentCommand(doc.Id, doc.IdTenant, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        indexer.Verify(x => x.ReindexAsync(doc, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenAutoIndexFails()
    {
        var (sut, indexer, _) = BuildSut(out var doc);
        indexer
            .Setup(x => x.ReindexAsync(doc, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding failed"));

        var result = await sut.Handle(
            new ApproveReviewedDocumentCommand(doc.Id, doc.IdTenant, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ReturnsHardBlocked_WhenBudgetGuardBlocks()
    {
        var (sut, _, guard) = BuildSut(out var doc);
        guard
            .Setup(x => x.CheckAsync(
                doc.IdTenant, doc.IdDepartment,
                doc.DocumentDate.Month, doc.DocumentDate.Year,
                It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult(
                BudgetCheckOutcome.BlockedByHardLimit,
                AvailableBefore: 0m, AvailableAfter: -1m,
                AllocatedAmount: 100m, CommittedAmount: 100m, SpentAmount: 0m,
                EnforcementMode: BudgetEnforcementMode.HardBlock,
                BudgetExists: true));

        var result = await sut.Handle(
            new ApproveReviewedDocumentCommand(doc.Id, doc.IdTenant, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BudgetErrors.HardBlocked, result.Error);
    }

    [Fact]
    public async Task Handle_ReturnsOverrideRequired_WhenSoftBlockOverBudget()
    {
        var (sut, _, guard) = BuildSut(out var doc);
        guard
            .Setup(x => x.CheckAsync(
                doc.IdTenant, doc.IdDepartment,
                doc.DocumentDate.Month, doc.DocumentDate.Year,
                It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult(
                BudgetCheckOutcome.RequiresOverride,
                AvailableBefore: 0m, AvailableAfter: -1m,
                AllocatedAmount: 100m, CommittedAmount: 100m, SpentAmount: 0m,
                EnforcementMode: BudgetEnforcementMode.SoftBlock,
                BudgetExists: true));

        var result = await sut.Handle(
            new ApproveReviewedDocumentCommand(doc.Id, doc.IdTenant, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BudgetErrors.OverrideRequired, result.Error);
    }

    private static (ApproveReviewedDocumentCommandHandler Handler, Mock<IReviewedDocumentChunkIndexer> Indexer, Mock<IBudgetGuard> Guard) BuildSut(out ReviewedDocument doc)
    {
        var docRepo = new Mock<IReviewedDocumentRepository>();
        var settingsRepo = new Mock<ITenantSettingsRepository>();
        var guard = new Mock<IBudgetGuard>();
        var reservation = new Mock<IBudgetReservationService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();
        var logger = new Mock<ILogger<ApproveReviewedDocumentCommandHandler>>();

        var d = CreateReviewedDocument();
        doc = d;

        docRepo
            .Setup(x => x.GetByIdForUpdateAsync(d.Id, d.IdTenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(d);

        // Default: no settings → permissive defaults (no escalation).
        settingsRepo
            .Setup(x => x.GetByTenantIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantSettings?)null);

        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        indexer.Setup(x => x.ReindexAsync(d, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        // Default: budget allows the approval.
        guard
            .Setup(x => x.CheckAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult(
                BudgetCheckOutcome.Allowed,
                AvailableBefore: 1m, AvailableAfter: 1m,
                AllocatedAmount: 0m, CommittedAmount: 0m, SpentAmount: 0m,
                EnforcementMode: BudgetEnforcementMode.Off,
                BudgetExists: false));

        reservation
            .Setup(x => x.CommitAsync(It.IsAny<BudgetMovement>(), It.IsAny<BudgetExceededTrigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ApproveReviewedDocumentCommandHandler(
            docRepo.Object,
            settingsRepo.Object,
            guard.Object,
            reservation.Object,
            unitOfWork.Object,
            indexer.Object,
            logger.Object);

        return (handler, indexer, guard);
    }

    private static ReviewedDocument CreateReviewedDocument()
    {
        var submitterMembershipId = Guid.NewGuid();
        var lineItem = ReviewedDocumentLineItem.Create("Laptop bag", 1m, 1500000m, 1500000m);
        return ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            submitterMembershipId,
            "receipt.pdf",
            "application/pdf",
            "Acme Supplies",
            "INV-001",
            new DateOnly(2026, 5, 10),
            "Equipment",
            null,
            1500000m,
            0m,
            1500000m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.UtcNow,
            [lineItem]).Value;
    }
}
