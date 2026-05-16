using FinFlow.Application.Documents.Commands.DeleteDocumentDraft;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class DeleteDocumentDraftCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid MembershipId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenOwnerDeletesActiveDraft()
    {
        var draft = CreateActiveDraft();
        var repo = new Mock<IUploadedDocumentDraftRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, TenantId, MembershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var uow = new Mock<IUnitOfWork>();
        var handler = new DeleteDocumentDraftCommandHandler(repo.Object, uow.Object, NullLogger<DeleteDocumentDraftCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeleteDocumentDraftCommand(draft.Id, TenantId, MembershipId, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(draft.IsActive);
        Assert.NotNull(draft.DeletedAt);
        repo.Verify(r => r.Update(draft), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDraftDoesNotExist()
    {
        var repo = new Mock<IUploadedDocumentDraftRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), TenantId, MembershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadedDocumentDraft?)null);
        var uow = new Mock<IUnitOfWork>();
        var handler = new DeleteDocumentDraftCommandHandler(repo.Object, uow.Object, NullLogger<DeleteDocumentDraftCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeleteDocumentDraftCommand(Guid.NewGuid(), TenantId, MembershipId, false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_ReturnsAlreadySubmitted_WhenDraftInactive()
    {
        var draft = CreateActiveDraft();
        draft.MarkSubmitted();
        var repo = new Mock<IUploadedDocumentDraftRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, TenantId, MembershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var uow = new Mock<IUnitOfWork>();
        var handler = new DeleteDocumentDraftCommandHandler(repo.Object, uow.Object, NullLogger<DeleteDocumentDraftCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeleteDocumentDraftCommand(draft.Id, TenantId, MembershipId, false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.AlreadySubmitted.Code, result.Error.Code);
    }

    private static UploadedDocumentDraft CreateActiveDraft()
    {
        var li = UploadedDocumentDraftLineItem.Create("Item", 1m, 200m, 200m).Value;
        return UploadedDocumentDraft.CreateSuggested(
            Guid.NewGuid(), TenantId, MembershipId,
            "invoice.pdf", "application/pdf",
            "Vendor", "INV-1",
            new DateOnly(2026, 5, 1), "SaaS", null,
            200m, 0m, 200m,
            "staff-upload", "staff@test.com", "High precision",
            DateTime.UtcNow, null, null,
            new[] { li }).Value;
    }
}
