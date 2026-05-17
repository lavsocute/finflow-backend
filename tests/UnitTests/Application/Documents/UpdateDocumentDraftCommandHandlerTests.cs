using FinFlow.Application.Documents.Commands.UpdateDocumentDraft;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class UpdateDocumentDraftCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid MembershipId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenOwnerUpdatesActiveDraft()
    {
        var draft = CreateActiveDraft();
        var repo = new Mock<IUploadedDocumentDraftRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, TenantId, MembershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var uow = new Mock<IUnitOfWork>();
        var handler = new UpdateDocumentDraftCommandHandler(repo.Object, uow.Object, new FinFlow.UnitTests.TestStubs.StubTenantRepository(), new FinFlow.UnitTests.TestStubs.StubExchangeRateService(), NullLogger<UpdateDocumentDraftCommandHandler>.Instance);

        var cmd = BuildCommand(draft.Id, isTenantOwner: false);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NewVendor", result.Value.VendorName);
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
        
        var handler = new UpdateDocumentDraftCommandHandler(repo.Object, uow.Object, new FinFlow.UnitTests.TestStubs.StubTenantRepository(), new FinFlow.UnitTests.TestStubs.StubExchangeRateService(), NullLogger<UpdateDocumentDraftCommandHandler>.Instance);

        var cmd = BuildCommand(Guid.NewGuid(), isTenantOwner: false);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_TenantOwner_UsesGetByTenantIdAsync()
    {
        var draft = CreateActiveDraft();
        var repo = new Mock<IUploadedDocumentDraftRepository>();
        repo.Setup(r => r.GetByTenantIdAsync(draft.Id, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var uow = new Mock<IUnitOfWork>();
        
        var handler = new UpdateDocumentDraftCommandHandler(repo.Object, uow.Object, new FinFlow.UnitTests.TestStubs.StubTenantRepository(), new FinFlow.UnitTests.TestStubs.StubExchangeRateService(), NullLogger<UpdateDocumentDraftCommandHandler>.Instance);

        var cmd = BuildCommand(draft.Id, isTenantOwner: true);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.GetByTenantIdAsync(draft.Id, TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsAlreadySubmitted_WhenDraftInactive()
    {
        var draft = CreateActiveDraft();
        draft.MarkSubmitted(); // makes IsActive = false
        var repo = new Mock<IUploadedDocumentDraftRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, TenantId, MembershipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var uow = new Mock<IUnitOfWork>();
        
        var handler = new UpdateDocumentDraftCommandHandler(repo.Object, uow.Object, new FinFlow.UnitTests.TestStubs.StubTenantRepository(), new FinFlow.UnitTests.TestStubs.StubExchangeRateService(), NullLogger<UpdateDocumentDraftCommandHandler>.Instance);

        var cmd = BuildCommand(draft.Id, isTenantOwner: false);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.AlreadySubmitted.Code, result.Error.Code);
    }

    private static UploadedDocumentDraft CreateActiveDraft()
    {
        var li = UploadedDocumentDraftLineItem.Create("Item", 1m, 200m, 200m).Value;
        return UploadedDocumentDraft.CreateSuggested(
            Guid.NewGuid(), TenantId, MembershipId,
            "invoice.pdf", "application/pdf",
            "OldVendor", "INV-1",
            new DateOnly(2026, 5, 1), "SaaS", null,
            200m, 0m, 200m,
            "staff-upload", "staff@test.com", "High precision",
            DateTime.UtcNow, null, null,
            new[] { li }).Value;
    }

    private static UpdateDocumentDraftCommand BuildCommand(Guid draftId, bool isTenantOwner) =>
        new(
            draftId, TenantId, MembershipId, isTenantOwner,
            "NewVendor", "INV-2", new DateOnly(2026, 5, 2), "SaaS", null,
            Subtotal: 300m, DocumentDiscountPercent: null, DocumentDiscountAmount: 0m,
            Vat: 0m, TotalAmount: 300m, ConfidenceLabel: "Edited",
            LineItems: new[] { new UpdateDocumentDraftLineItem("NewItem", 1m, 300m, null, 0m, 300m) });
}
