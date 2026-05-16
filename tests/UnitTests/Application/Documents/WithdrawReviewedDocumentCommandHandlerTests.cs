using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Documents.Commands.WithdrawReviewedDocument;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Expenses;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class WithdrawReviewedDocumentCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();
    private static readonly Guid MembershipId = Guid.NewGuid();

    private static WithdrawReviewedDocumentCommandHandler BuildHandler(
        Mock<IReviewedDocumentRepository> docRepo,
        Mock<IUploadedDocumentDraftRepository> draftRepo,
        Mock<IPaymentRepository> paymentRepo,
        Mock<IUnitOfWork> uow,
        Mock<IReviewedDocumentChunkIndexer> indexer)
        => new(
            docRepo.Object, draftRepo.Object, paymentRepo.Object,
            uow.Object, indexer.Object,
            NullLogger<WithdrawReviewedDocumentCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenOwnerWithdrawsReadyForApproval()
    {
        var doc = CreateReadyDoc();
        var docRepo = SetupDocRepo(doc);
        var draftRepo = new Mock<IUploadedDocumentDraftRepository>();
        draftRepo.Setup(r => r.GetByIdAsync(doc.Id, TenantId, MembershipId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadedDocumentDraft?)null);
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.ExistsByDocumentIdAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var uow = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();

        var handler = BuildHandler(docRepo, draftRepo, paymentRepo, uow, indexer);

        var result = await handler.Handle(
            new WithdrawReviewedDocumentCommand(doc.Id, TenantId, MembershipId, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Draft", result.Value.Status);
        draftRepo.Verify(r => r.Add(It.IsAny<UploadedDocumentDraft>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        indexer.Verify(i => i.RemoveAsync(doc.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDocDoesNotExist()
    {
        var docRepo = new Mock<IReviewedDocumentRepository>();
        docRepo.Setup(r => r.GetByIdForUpdateAsync(It.IsAny<Guid>(), TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReviewedDocument?)null);
        var draftRepo = new Mock<IUploadedDocumentDraftRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var uow = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();

        var handler = BuildHandler(docRepo, draftRepo, paymentRepo, uow, indexer);

        var result = await handler.Handle(
            new WithdrawReviewedDocumentCommand(Guid.NewGuid(), TenantId, MembershipId, false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenCallerIsNotOwner()
    {
        var doc = CreateReadyDoc();
        var docRepo = SetupDocRepo(doc);
        var otherMembership = Guid.NewGuid();
        var draftRepo = new Mock<IUploadedDocumentDraftRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var uow = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();

        var handler = BuildHandler(docRepo, draftRepo, paymentRepo, uow, indexer);

        var result = await handler.Handle(
            new WithdrawReviewedDocumentCommand(doc.Id, TenantId, otherMembership, false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.Unauthorized.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsWithdrawnHasPayment_WhenPaymentExists()
    {
        var doc = CreateReadyDoc();
        var docRepo = SetupDocRepo(doc);
        var draftRepo = new Mock<IUploadedDocumentDraftRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.ExistsByDocumentIdAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var uow = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();

        var handler = BuildHandler(docRepo, draftRepo, paymentRepo, uow, indexer);

        var result = await handler.Handle(
            new WithdrawReviewedDocumentCommand(doc.Id, TenantId, MembershipId, false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.WithdrawnHasPayment.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsCannotWithdraw_WhenDocAlreadyApproved()
    {
        var doc = CreateReadyDoc();
        doc.Approve();
        var docRepo = SetupDocRepo(doc);
        var draftRepo = new Mock<IUploadedDocumentDraftRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.ExistsByDocumentIdAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var uow = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();

        var handler = BuildHandler(docRepo, draftRepo, paymentRepo, uow, indexer);

        var result = await handler.Handle(
            new WithdrawReviewedDocumentCommand(doc.Id, TenantId, MembershipId, false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.CannotWithdraw.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_ContinuesOnChunkRemovalFailure()
    {
        var doc = CreateReadyDoc();
        var docRepo = SetupDocRepo(doc);
        var draftRepo = new Mock<IUploadedDocumentDraftRepository>();
        draftRepo.Setup(r => r.GetByIdAsync(doc.Id, TenantId, MembershipId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadedDocumentDraft?)null);
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.ExistsByDocumentIdAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var uow = new Mock<IUnitOfWork>();
        var indexer = new Mock<IReviewedDocumentChunkIndexer>();
        indexer.Setup(i => i.RemoveAsync(doc.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vector store unavailable"));

        var handler = BuildHandler(docRepo, draftRepo, paymentRepo, uow, indexer);

        var result = await handler.Handle(
            new WithdrawReviewedDocumentCommand(doc.Id, TenantId, MembershipId, false),
            CancellationToken.None);

        // Should still succeed — chunk removal is best-effort
        Assert.True(result.IsSuccess);
    }

    private static ReviewedDocument CreateReadyDoc() =>
        ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(), TenantId, DepartmentId, MembershipId,
            "invoice.pdf", "application/pdf",
            "Vendor", "INV-1",
            new DateOnly(2026, 5, 1), "SaaS", null,
            200m, 0m, 200m,
            "staff-upload", "staff@test.com", "Staff corrected",
            DateTime.UtcNow,
            new[] { ReviewedDocumentLineItem.Create("Item", 1m, 200m, 200m) }).Value;

    private static Mock<IReviewedDocumentRepository> SetupDocRepo(ReviewedDocument doc)
    {
        var repo = new Mock<IReviewedDocumentRepository>();
        repo.Setup(r => r.GetByIdForUpdateAsync(doc.Id, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
        return repo;
    }
}
