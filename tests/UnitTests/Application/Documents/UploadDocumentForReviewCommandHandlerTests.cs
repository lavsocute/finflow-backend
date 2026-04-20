using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Documents.Commands.UploadDocumentForReview;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class UploadDocumentForReviewCommandHandlerTests
{
    [Fact]
    public async Task Handle_UsesOcrServiceResult_ToPersistAndReturnDraft()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var fileName = "invoice-2026-04.pdf";
        var contentType = "application/pdf";
        var fileContents = new byte[] { 1, 2, 3, 4 };

        var ocrResult = Result.Success(new OcrExtractionResult(
            "Acme Cloud Ltd.",
            "INV-2026-0042",
            new DateOnly(2026, 4, 18),
            new DateOnly(2026, 5, 2),
            "Software & SaaS",
            "TX-123",
            1200.00m,
            240.00m,
            1440.00m,
            "ocr-provider",
            "High precision",
            [
                new OcrExtractionLineItem("Cloud Compute Instance", 1, 850.00m, 850.00m),
                new OcrExtractionLineItem("Support Plan", 1, 590.00m, 590.00m)
            ]));

        var ocrService = new StubOcrExtractionService(ocrResult);
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                tenantId,
                membershipId,
                "reviewer@finflow.test",
                fileName,
                contentType,
                fileContents),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(fileName, result.Value.OriginalFileName);
        Assert.Equal(contentType, result.Value.ContentType);
        Assert.Equal("Acme Cloud Ltd.", result.Value.VendorName);
        Assert.Equal("INV-2026-0042", result.Value.Reference);
        Assert.Equal(new DateOnly(2026, 4, 18), result.Value.DocumentDate);
        Assert.Equal(new DateOnly(2026, 5, 2), result.Value.DueDate);
        Assert.Equal("Software & SaaS", result.Value.Category);
        Assert.Equal("TX-123", result.Value.VendorTaxId);
        Assert.Equal(1200.00m, result.Value.Subtotal);
        Assert.Equal(240.00m, result.Value.Vat);
        Assert.Equal(1440.00m, result.Value.TotalAmount);
        Assert.Equal("ocr-provider", result.Value.Source);
        Assert.Equal("reviewer@finflow.test", result.Value.ReviewedByStaff);
        Assert.Equal("High precision", result.Value.ConfidenceLabel);
        Assert.Equal(2, result.Value.LineItems.Count);
        Assert.Equal("Cloud Compute Instance", result.Value.LineItems[0].ItemName);
        Assert.Equal(850.00m, result.Value.LineItems[0].Total);
        Assert.Equal("Support Plan", result.Value.LineItems[1].ItemName);
        Assert.Equal(590.00m, result.Value.LineItems[1].Total);

        Assert.True(ocrService.WasCalled);
        Assert.Equal(fileName, ocrService.FileName);
        Assert.Equal(contentType, ocrService.ContentType);
        Assert.Equal(fileContents, ocrService.FileContents);
        Assert.Single(repository.AddedDrafts);
        var persistedDraft = repository.AddedDrafts[0];
        Assert.Equal(persistedDraft.Id, result.Value.DocumentId);
        Assert.Equal(tenantId, persistedDraft.IdTenant);
        Assert.Equal(membershipId, persistedDraft.MembershipId);
        Assert.Equal(fileName, persistedDraft.OriginalFileName);
        Assert.Equal(contentType, persistedDraft.ContentType);
        Assert.Equal("Acme Cloud Ltd.", persistedDraft.VendorName);
        Assert.Equal("INV-2026-0042", persistedDraft.Reference);
        Assert.Equal(new DateOnly(2026, 4, 18), persistedDraft.DocumentDate);
        Assert.Equal(new DateOnly(2026, 5, 2), persistedDraft.DueDate);
        Assert.Equal("Software & SaaS", persistedDraft.Category);
        Assert.Equal("TX-123", persistedDraft.VendorTaxId);
        Assert.Equal(1200.00m, persistedDraft.Subtotal);
        Assert.Equal(240.00m, persistedDraft.Vat);
        Assert.Equal(1440.00m, persistedDraft.TotalAmount);
        Assert.Equal("ocr-provider", persistedDraft.Source);
        Assert.Equal("reviewer@finflow.test", persistedDraft.UploadedByStaff);
        Assert.Equal("High precision", persistedDraft.ConfidenceLabel);
        Assert.Equal(2, persistedDraft.LineItems.Count);
        Assert.Contains(persistedDraft.LineItems, item => item.ItemName == "Cloud Compute Instance" && item.Total == 850.00m);
        Assert.Contains(persistedDraft.LineItems, item => item.ItemName == "Support Plan" && item.Total == 590.00m);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsNormalizedResponse_FromPersistedDraft()
    {
        var ocrResult = Result.Success(new OcrExtractionResult(
            "  Acme Cloud Ltd.  ",
            "  INV-2026-0042  ",
            new DateOnly(2026, 4, 18),
            new DateOnly(2026, 5, 2),
            "  Software & SaaS  ",
            "  TX-123  ",
            1200.00m,
            240.00m,
            1440.00m,
            "   ",
            "   ",
            [
                new OcrExtractionLineItem("  Cloud Compute Instance  ", 1, 1200.00m, 1200.00m),
                new OcrExtractionLineItem("  Tax Adjustment  ", 1, 240.00m, 240.00m)
            ]));
        var ocrService = new StubOcrExtractionService(ocrResult);
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Acme Cloud Ltd.", result.Value.VendorName);
        Assert.Equal("INV-2026-0042", result.Value.Reference);
        Assert.Equal("Software & SaaS", result.Value.Category);
        Assert.Equal("TX-123", result.Value.VendorTaxId);
        Assert.Equal("staff-upload", result.Value.Source);
        Assert.Equal("High precision", result.Value.ConfidenceLabel);
        Assert.Equal(2, result.Value.LineItems.Count);
        Assert.Equal("Cloud Compute Instance", result.Value.LineItems[0].ItemName);
    }

    [Fact]
    public async Task Handle_ReturnsOcrFailure_WithoutPersistingDraft()
    {
        var ocrError = new Error("Ocr.ExtractionFailed", "OCR failed.");
        var ocrService = new StubOcrExtractionService(Result.Failure<OcrExtractionResult>(ocrError));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ocrError, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.True(ocrService.WasCalled);
    }

    [Fact]
    public async Task Handle_ReturnsUnsupportedContentType_BeforeCallingOcrOrPersisting()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(new OcrExtractionResult(
                "Acme Cloud Ltd.",
                "INV-2026-0042",
                new DateOnly(2026, 4, 18),
                new DateOnly(2026, 5, 2),
                "Software & SaaS",
                null,
                1200.00m,
                240.00m,
                1440.00m,
                "ocr-provider",
                "High precision",
                [new OcrExtractionLineItem("Cloud Compute Instance", 1, 850.00m, 850.00m)])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.txt",
                "text/plain",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.UnsupportedContentType, result.Error);
        Assert.False(ocrService.WasCalled);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenOcrSuccessPayloadHasNullLineItems()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(new OcrExtractionResult(
                "Acme Cloud Ltd.",
                "INV-2026-0042",
                new DateOnly(2026, 4, 18),
                new DateOnly(2026, 5, 2),
                "Software & SaaS",
                null,
                1200.00m,
                240.00m,
                1440.00m,
                "ocr-provider",
                "High precision",
                null!)));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemRequired, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenOcrSuccessPayloadHasEmptyLineItems()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemRequired, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenOcrSuccessPayloadHasBlankLineItemName()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("   ", 1m, 850.00m, 850.00m)
            ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemNameRequired, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenOcrSuccessPayloadHasNonPositiveLineItemQuantity()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("Cloud Compute Instance", 0m, 850.00m, 850.00m)
            ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemQuantityInvalid, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenOcrSuccessPayloadHasNonPositiveLineItemUnitPrice()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("Cloud Compute Instance", 1m, 0m, 850.00m)
            ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemUnitPriceInvalid, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenOcrSuccessPayloadHasNonPositiveLineItemTotal()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("Cloud Compute Instance", 1m, 850.00m, 0m)
            ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemTotalInvalid, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenOcrSuccessPayloadHasLineItemTotalsMismatch()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("Cloud Compute Instance", 1m, 850.00m, 840.00m),
                new OcrExtractionLineItem("Support Plan", 1m, 590.00m, 590.00m)
            ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemTotalsMismatch, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenOcrSuccessPayloadHasSubtotalVatMismatch()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("Cloud Compute Instance", 1m, 850.00m, 850.00m),
                new OcrExtractionLineItem("Support Plan", 1m, 590.00m, 590.00m)
            ], subtotal: 1000.00m, vat: 240.00m, totalAmount: 1440.00m)));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.FinancialBreakdownMismatch, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_PropagatesDraftCreationFailure_WhenOcrVendorNameIsBlank()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(new OcrExtractionResult(
                "   ",
                "INV-2026-0042",
                new DateOnly(2026, 4, 18),
                new DateOnly(2026, 5, 2),
                "Software & SaaS",
                "TX-123",
                1200.00m,
                240.00m,
                1440.00m,
                "ocr-provider",
                "High precision",
                [
                    new OcrExtractionLineItem("Cloud Compute Instance", 1m, 1200.00m, 1200.00m),
                    new OcrExtractionLineItem("Tax Adjustment", 1m, 240.00m, 240.00m)
                ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.VendorNameRequired, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_PropagatesDraftCreationFailure_WhenUploaderEmailIsBlank()
    {
        var ocrService = new StubOcrExtractionService(Result.Success(CreateValidOcrResult([
            new OcrExtractionLineItem("Cloud Compute Instance", 1m, 1200.00m, 1200.00m),
            new OcrExtractionLineItem("Tax Adjustment", 1m, 240.00m, 240.00m)
        ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "   ",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.UploadedByRequired, result.Error);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static OcrExtractionResult CreateValidOcrResult(
        IReadOnlyList<OcrExtractionLineItem> lineItems,
        decimal subtotal = 1200.00m,
        decimal vat = 240.00m,
        decimal totalAmount = 1440.00m)
        => new(
            "Acme Cloud Ltd.",
            "INV-2026-0042",
            new DateOnly(2026, 4, 18),
            new DateOnly(2026, 5, 2),
            "Software & SaaS",
            "TX-123",
            subtotal,
            vat,
            totalAmount,
            "ocr-provider",
            "High precision",
            lineItems);

    [Fact]
    public async Task Handle_ReturnsCanonicalFileNameRequiredError_WhenFileNameIsBlank()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([new OcrExtractionLineItem("Cloud Compute Instance", 1, 850.00m, 850.00m)])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "   ",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.FileNameRequired, result.Error);
        Assert.False(ocrService.WasCalled);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private sealed class StubOcrExtractionService : IOcrExtractionService
    {
        private readonly Result<OcrExtractionResult> _result;

        public StubOcrExtractionService(Result<OcrExtractionResult> result) => _result = result;

        public bool WasCalled { get; private set; }
        public string? FileName { get; private set; }
        public string? ContentType { get; private set; }
        public byte[]? FileContents { get; private set; }

        public Task<Result<OcrExtractionResult>> ExtractAsync(
            string fileName,
            string contentType,
            byte[] fileContents,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            FileName = fileName;
            ContentType = contentType;
            FileContents = fileContents;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubUploadedDocumentDraftRepository : IUploadedDocumentDraftRepository
    {
        public List<UploadedDocumentDraft> AddedDrafts { get; } = [];

        public void Add(UploadedDocumentDraft draft) => AddedDrafts.Add(draft);
        public void Update(UploadedDocumentDraft draft) => throw new NotSupportedException();
        public Task<UploadedDocumentDraft?> GetByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default)
            => Task.FromResult<UploadedDocumentDraft?>(null);
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<UploadedDocumentDraft>> GetOwnedActiveAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UploadedDocumentDraft>>([]);
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }
}
