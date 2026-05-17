using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions;
using FinFlow.Application.Documents.Commands.UploadDocumentForReview;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Application.Documents.Ocr;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;

namespace FinFlow.UnitTests.Application.Documents;

public sealed class UploadDocumentForReviewCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsQuotaExceeded_WhenMemberMonthlyOcrQuotaIsExhausted()
    {
        var ocrService = new StubOcrExtractionService(Result.Success(CreateValidOcrResult([
            new OcrExtractionLineItem("Cloud Compute Instance", 1m, 1200.00m, 1200.00m),
            new OcrExtractionLineItem("Tax Adjustment", 1m, 240.00m, 240.00m)
        ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var subscriptionQuotaGate = new MemberQuotaExceededSubscriptionQuotaGate();
        var handler = new UploadDocumentForReviewCommandHandler(
            repository,
            unitOfWork,
            ocrService,
            subscriptionQuotaGate,
            new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        Assert.Equal("Subscription.OcrMemberQuotaExceeded", result.Error.Code);
        Assert.False(ocrService.WasCalled);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Equal(1, subscriptionQuotaGate.EnsureOcrAllowedCallCount);
        Assert.Equal(1, subscriptionQuotaGate.LastRequestedPageCount);
        Assert.Equal(0, subscriptionQuotaGate.RecordOcrUsageCallCount);
    }

    [Fact]
    public async Task Handle_ReturnsOcrNotAvailableForCurrentPlan_WhenTenantPlanDoesNotAllowOcr()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(new OcrExtractionResult(
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
                [new OcrExtractionLineItem("Cloud Compute Instance", 1, 850.00m, 850.00m)], 1)));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();

        var handler = (UploadDocumentForReviewCommandHandler)Activator.CreateInstance(
            typeof(UploadDocumentForReviewCommandHandler),
            repository,
            unitOfWork,
            ocrService,
            new DeniedSubscriptionQuotaGate(),
            new NoOpDocumentStorageProvider(),
            new StubTenantRepositoryWithCurrency("VND"),
            new StubExchangeRateService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance)!;

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
        Assert.Equal("Documents.OcrNotAvailableForCurrentPlan", result.Error.Code);
        Assert.False(ocrService.WasCalled);
        Assert.Empty(repository.AddedDrafts);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_UsesOcrServiceResult_ToPersistAndReturnDraft()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
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
            ], 1));

        var ocrService = new StubOcrExtractionService(ocrResult);
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var subscriptionQuotaGate = new TrackingSubscriptionQuotaGate(tenantId, membershipId);
        var handler = new UploadDocumentForReviewCommandHandler(
            repository,
            unitOfWork,
            ocrService,
            subscriptionQuotaGate,
            new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        Assert.Equal(1, subscriptionQuotaGate.EnsureOcrAllowedCallCount);
        Assert.Equal(1, subscriptionQuotaGate.LastRequestedPageCount);
        Assert.Equal(1, subscriptionQuotaGate.RecordOcrUsageCallCount);
        Assert.NotNull(subscriptionQuotaGate.RecordedDecision);
        Assert.Equal(tenantId, subscriptionQuotaGate.RecordedDecision!.TenantId);
        Assert.Equal(membershipId, subscriptionQuotaGate.RecordedDecision.MembershipId);
        Assert.Equal(SubscriptionFeature.DocumentsOcr, subscriptionQuotaGate.RecordedDecision.Feature);
        Assert.Equal(1, subscriptionQuotaGate.RecordedDecision.ApprovedUnitCount);
        Assert.Equal(subscriptionQuotaGate.PeriodStart, subscriptionQuotaGate.RecordedDecision.PeriodStart);
        Assert.Equal(subscriptionQuotaGate.PeriodEnd, subscriptionQuotaGate.RecordedDecision.PeriodEnd);
    }

    [Fact]
    public async Task Handle_ReturnsNormalizedResponse_FromPersistedDraft()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionResult = TenantSubscription.Create(tenantId, PlanTier.Pro, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(1));
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
            ], 1));
        var ocrService = new StubOcrExtractionService(ocrResult);
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(
            repository,
            unitOfWork,
            ocrService,
            new AllowAllSubscriptionQuotaGate(),
            new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
                [new OcrExtractionLineItem("Cloud Compute Instance", 1, 850.00m, 850.00m)], 1)));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
                null!, 1)));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
    public async Task Handle_CreatesDraft_WhenOcrSuccessPayloadHasRoundedLineItemTotalsMismatch()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionResult = TenantSubscription.Create(tenantId, PlanTier.Pro, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(1));
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("Cloud Compute Instance", 1m, 850.00m, 840.00m),
                new OcrExtractionLineItem("Support Plan", 1m, 590.00m, 590.00m)
            ])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(
            repository,
            unitOfWork,
            ocrService,
            new AllowAllSubscriptionQuotaGate(),
            new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                tenantId,
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Single(repository.AddedDrafts);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_CreatesDraft_WhenOcrSuccessPayloadHasLineItemTotalsMismatch()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionResult = TenantSubscription.Create(tenantId, PlanTier.Pro, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(1));
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("Rounded OCR Item", 3m, 33.33m, 100.00m)
            ], subtotal: 100.00m, vat: 0m, totalAmount: 100.00m)));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(
            repository,
            unitOfWork,
            ocrService,
            new AllowAllSubscriptionQuotaGate(),
            new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                tenantId,
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.jpg",
                "image/jpeg",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Single(repository.AddedDrafts);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(100.00m, result.Value.TotalAmount);
        Assert.Equal(100.00m, result.Value.LineItems.Single().Total);
    }

    [Fact]
    public async Task Handle_CreatesDraft_WhenOcrSuccessPayloadHasSubtotalVatMismatch()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionResult = TenantSubscription.Create(tenantId, PlanTier.Pro, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(1));
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([
                new OcrExtractionLineItem("Cloud Compute Instance", 1m, 850.00m, 850.00m),
                new OcrExtractionLineItem("Support Plan", 1m, 590.00m, 590.00m)
            ], subtotal: 1000.00m, vat: 240.00m, totalAmount: 1440.00m)));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(
            repository,
            unitOfWork,
            ocrService,
            new AllowAllSubscriptionQuotaGate(),
            new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

        var result = await handler.Handle(
            new UploadDocumentForReviewCommand(
                Guid.NewGuid(),
                tenantId,
                Guid.NewGuid(),
                "reviewer@finflow.test",
                "invoice.pdf",
                "application/pdf",
                [1, 2, 3]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Single(repository.AddedDrafts);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
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
                ], 1)));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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
        decimal totalAmount = 1440.00m,
        int processedPageCount = 1)
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
            lineItems,
            processedPageCount);

    [Fact]
    public async Task Handle_ReturnsCanonicalFileNameRequiredError_WhenFileNameIsBlank()
    {
        var ocrService = new StubOcrExtractionService(
            Result.Success(CreateValidOcrResult([new OcrExtractionLineItem("Cloud Compute Instance", 1, 850.00m, 850.00m)])));
        var repository = new StubUploadedDocumentDraftRepository();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UploadDocumentForReviewCommandHandler(repository, unitOfWork, ocrService, new AllowAllSubscriptionQuotaGate(), new NoOpDocumentStorageProvider(), new StubTenantRepositoryWithCurrency("VND"), new StubExchangeRateService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadDocumentForReviewCommandHandler>.Instance);

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

        public Task<Result<int>> GetPageCountAsync(
            string contentType,
            byte[] fileContents,
            CancellationToken cancellationToken)
        {
            if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Result.Success(1));

            return Task.FromResult(Result.Success(1));
        }
    }

    private sealed class StubUploadedDocumentDraftRepository : IUploadedDocumentDraftRepository
    {
        public List<UploadedDocumentDraft> AddedDrafts { get; } = [];

        public void Add(UploadedDocumentDraft draft) => AddedDrafts.Add(draft);
        public void Update(UploadedDocumentDraft draft) => throw new NotSupportedException();
        public Task<UploadedDocumentDraft?> GetByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default)
            => Task.FromResult<UploadedDocumentDraft?>(null);
        public Task<UploadedDocumentDraft?> GetByIdAsync(Guid id, Guid tenantId, Guid membershipId, bool includeInactive, CancellationToken cancellationToken = default)
            => Task.FromResult<UploadedDocumentDraft?>(null);
        public Task<UploadedDocumentDraft?> GetByTenantIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
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

    private sealed class DeniedSubscriptionQuotaGate : ISubscriptionQuotaGate
    {
        public Task<Result<SubscriptionQuotaDecision>> EnsureChatbotAllowedAsync(Guid tenantId, Guid membershipId, int messageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure<SubscriptionQuotaDecision>(new Error("Subscription.ChatbotNotAvailableForCurrentPlan", "Chatbot is not available for the current plan.")));

        public Task<Result<SubscriptionQuotaDecision>> EnsureOcrAllowedAsync(Guid tenantId, Guid membershipId, int pageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure<SubscriptionQuotaDecision>(new Error("Documents.OcrNotAvailableForCurrentPlan", "OCR is not available for the current plan.")));

        public Task RecordChatbotUsageAsync(SubscriptionQuotaDecision decision, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result> EnsureChatbotTokensAvailableAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        public Task RecordChatbotTokensAsync(Guid tenantId, Guid membershipId, long tokensUsed, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RecordOcrUsageAsync(SubscriptionQuotaDecision decision, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class TrackingSubscriptionQuotaGate : ISubscriptionQuotaGate
    {
        private readonly SubscriptionQuotaDecision _decision;

        public TrackingSubscriptionQuotaGate(Guid tenantId, Guid membershipId)
        {
            _decision = new SubscriptionQuotaDecision(
                tenantId,
                membershipId,
                PeriodStart,
                PeriodEnd,
                SubscriptionFeature.DocumentsOcr,
                1,
                new PlanEntitlements(true, true, true, 10L * 1024 * 1024 * 1024, 1_000, 100, 10_000, 500),
                0,
                0,
                0,
                0);
        }

        public DateOnly PeriodStart { get; } = new(2026, 4, 1);
        public DateOnly PeriodEnd { get; } = new(2026, 4, 30);
        public int EnsureOcrAllowedCallCount { get; private set; }
        public int LastRequestedPageCount { get; private set; }
        public int RecordOcrUsageCallCount { get; private set; }
        public SubscriptionQuotaDecision? RecordedDecision { get; private set; }

        public Task<Result<SubscriptionQuotaDecision>> EnsureChatbotAllowedAsync(Guid tenantId, Guid membershipId, int messageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure<SubscriptionQuotaDecision>(new Error("Subscription.ChatbotNotAvailableForCurrentPlan", "Chatbot is not available for the current plan.")));

        public Task<Result<SubscriptionQuotaDecision>> EnsureOcrAllowedAsync(Guid tenantId, Guid membershipId, int pageCount, CancellationToken cancellationToken)
        {
            EnsureOcrAllowedCallCount++;
            LastRequestedPageCount = pageCount;
            return Task.FromResult(Result.Success(_decision with { ApprovedUnitCount = pageCount }));
        }

        public Task RecordChatbotUsageAsync(SubscriptionQuotaDecision decision, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result> EnsureChatbotTokensAvailableAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        public Task RecordChatbotTokensAsync(Guid tenantId, Guid membershipId, long tokensUsed, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RecordOcrUsageAsync(SubscriptionQuotaDecision decision, CancellationToken cancellationToken)
        {
            RecordOcrUsageCallCount++;
            RecordedDecision = decision;
            return Task.CompletedTask;
        }
    }

    private sealed class MemberQuotaExceededSubscriptionQuotaGate : ISubscriptionQuotaGate
    {
        public int EnsureOcrAllowedCallCount { get; private set; }
        public int LastRequestedPageCount { get; private set; }
        public int RecordOcrUsageCallCount { get; private set; }

        public Task<Result<SubscriptionQuotaDecision>> EnsureChatbotAllowedAsync(Guid tenantId, Guid membershipId, int messageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure<SubscriptionQuotaDecision>(new Error("Subscription.ChatbotNotAvailableForCurrentPlan", "Chatbot is not available for the current plan.")));

        public Task<Result<SubscriptionQuotaDecision>> EnsureOcrAllowedAsync(Guid tenantId, Guid membershipId, int pageCount, CancellationToken cancellationToken)
        {
            EnsureOcrAllowedCallCount++;
            LastRequestedPageCount = pageCount;
            return Task.FromResult(Result.Failure<SubscriptionQuotaDecision>(new Error("Subscription.OcrMemberQuotaExceeded", "The current member has reached the monthly OCR quota.")));
        }

        public Task RecordChatbotUsageAsync(SubscriptionQuotaDecision decision, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result> EnsureChatbotTokensAvailableAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        public Task RecordChatbotTokensAsync(Guid tenantId, Guid membershipId, long tokensUsed, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RecordOcrUsageAsync(SubscriptionQuotaDecision decision, CancellationToken cancellationToken)
        {
            RecordOcrUsageCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class AllowAllSubscriptionQuotaGate : ISubscriptionQuotaGate
    {
        public Task<Result<SubscriptionQuotaDecision>> EnsureChatbotAllowedAsync(Guid tenantId, Guid membershipId, int messageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(CreateDecision(tenantId, membershipId, SubscriptionFeature.Chatbot, messageCount)));

        public Task<Result<SubscriptionQuotaDecision>> EnsureOcrAllowedAsync(Guid tenantId, Guid membershipId, int pageCount, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(CreateDecision(tenantId, membershipId, SubscriptionFeature.DocumentsOcr, pageCount)));

        public Task RecordChatbotUsageAsync(SubscriptionQuotaDecision decision, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result> EnsureChatbotTokensAvailableAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        public Task RecordChatbotTokensAsync(Guid tenantId, Guid membershipId, long tokensUsed, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RecordOcrUsageAsync(SubscriptionQuotaDecision decision, CancellationToken cancellationToken)
            => Task.CompletedTask;

        private static SubscriptionQuotaDecision CreateDecision(Guid tenantId, Guid membershipId, SubscriptionFeature feature, int approvedUnitCount) =>
            new(
                tenantId,
                membershipId,
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 4, 30),
                feature,
                approvedUnitCount,
                new PlanEntitlements(true, true, true, long.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
                0,
                0,
                0,
                0);
    }

    private sealed class NoOpDocumentStorageProvider : IDocumentStorageProvider
    {
        public Task SaveImageAsync(Guid documentId, byte[] imageData, string contentType, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<byte[]?> GetImageAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public Task DeleteImageAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetContentTypeAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class StubTenantRepositoryWithCurrency : FinFlow.Domain.Tenants.ITenantRepository
    {
        private readonly string _currency;
        public StubTenantRepositoryWithCurrency(string currency) => _currency = currency;

        public Task<FinFlow.Domain.Tenants.TenantSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<FinFlow.Domain.Tenants.TenantSummary?>(
                new FinFlow.Domain.Tenants.TenantSummary(id, "Test", "test", FinFlow.Domain.Enums.TenancyModel.Shared, true, _currency));

        public Task<IReadOnlyList<FinFlow.Domain.Tenants.TenantSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FinFlow.Domain.Tenants.TenantSummary>>(Array.Empty<FinFlow.Domain.Tenants.TenantSummary>());

        public Task<FinFlow.Domain.Tenants.TenantSummary?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<FinFlow.Domain.Tenants.TenantSummary?>(null);

        public Task<bool> ExistsByCodeAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IReadOnlyList<FinFlow.Domain.Tenants.TenantSummary>> GetAllActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FinFlow.Domain.Tenants.TenantSummary>>(Array.Empty<FinFlow.Domain.Tenants.TenantSummary>());

        public void Add(FinFlow.Domain.Entities.Tenant tenant) { }
        public void Update(FinFlow.Domain.Entities.Tenant tenant) { }
        public void Remove(FinFlow.Domain.Entities.Tenant tenant) { }
    }

    private sealed class StubExchangeRateService : FinFlow.Application.Common.ExchangeRates.IExchangeRateService
    {
        public Task<FinFlow.Domain.Abstractions.Result<FinFlow.Application.Common.ExchangeRates.ExchangeRateLookupResult>> GetRateAsync(
            string fromCurrency, string toCurrency, DateOnly rateDate, CancellationToken cancellationToken = default)
        {
            // Simple stub: same currency = 1.0; otherwise default to a fake rate.
            var rate = string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase)
                ? 1m
                : 25_000m;
            return Task.FromResult(FinFlow.Domain.Abstractions.Result.Success(
                new FinFlow.Application.Common.ExchangeRates.ExchangeRateLookupResult(
                    rate, rateDate, FinFlow.Domain.ExchangeRates.ExchangeRateSource.System)));
        }
    }
}
