using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests.Domain;

public sealed class UploadedDocumentDraftTests
{
    private static UploadedDocumentDraftLineItem Line(decimal q, decimal up, decimal? pct, decimal disc, decimal total)
        => UploadedDocumentDraftLineItem.Create("Item", q, up, pct, disc, total).Value;

    private static UploadedDocumentDraft NewDraft(decimal? docPercent = null, decimal docDisc = 0m)
    {
        // UploadedDocumentDraft enforces: Subtotal − DocDiscount + Vat = Total.
        // Pick: subtotal=200, vat=0, total = subtotal − docDisc.
        var subtotal = 200m;
        var vat = 0m;
        var total = subtotal - docDisc + vat;
        var result = UploadedDocumentDraft.CreateSuggested(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "invoice.pdf", "application/pdf",
            "Vendor", "INV-1",
            new DateOnly(2026, 5, 1),
            "SaaS", null,
            subtotal: subtotal,
            documentDiscountPercent: docPercent,
            documentDiscountAmount: docDisc,
            vat: vat,
            totalAmount: total,
            "staff-upload",
            "staff@finflow.test",
            "High precision",
            DateTime.SpecifyKind(new DateTime(2026, 5, 1, 9, 0, 0), DateTimeKind.Utc),
            null,
            null,
            new[] { Line(1m, total, null, 0m, total) });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : "");
        return result.Value;
    }

    [Fact]
    public void CreateSuggested_NoDocDiscount_Succeeds()
    {
        var draft = NewDraft();

        Assert.True(draft.IsActive);
        Assert.Null(draft.DocumentDiscountPercent);
        Assert.Equal(0m, draft.DocumentDiscountAmount);
    }

    [Fact]
    public void CreateSuggested_WithDocDiscount_Succeeds()
    {
        // subtotal=200, docPct=10 ⇒ docDisc=20; vat=0; total = 200-20+0 = 180
        var docDisc = 20m;
        var docPct = 10m;
        var draft = NewDraft(docPercent: docPct, docDisc: docDisc);

        Assert.Equal(docPct, draft.DocumentDiscountPercent);
        Assert.Equal(docDisc, draft.DocumentDiscountAmount);
    }

    [Fact]
    public void CreateSuggested_DocDiscountExceedsSubtotal_Fails()
    {
        var result = UploadedDocumentDraft.CreateSuggested(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "invoice.pdf", "application/pdf",
            "Vendor", "INV-1",
            new DateOnly(2026, 5, 1),
            "SaaS", null,
            subtotal: 100m,
            documentDiscountPercent: null,
            documentDiscountAmount: 200m,
            vat: 0m,
            totalAmount: 0m,
            "staff-upload",
            "staff@finflow.test",
            "High precision",
            DateTime.SpecifyKind(new DateTime(2026, 5, 1, 9, 0, 0), DateTimeKind.Utc),
            null, null,
            new[] { Line(1m, 100m, null, 0m, 100m) });

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.DocumentDiscountExceedsSubtotal.Code, result.Error.Code);
    }

    [Fact]
    public void UpdateDraftFields_Active_Succeeds()
    {
        var draft = NewDraft();
        // legacy: subtotal=300, vat=30, total=330, ΣLine=330
        var newLines = new[] { Line(1m, 330m, null, 0m, 330m) };

        var result = draft.UpdateDraftFields(
            "NewVendor", "INV-2", new DateOnly(2026, 5, 2), "SaaS", null,
            subtotal: 300m, documentDiscountPercent: null, documentDiscountAmount: 0m,
            vat: 30m, totalAmount: 330m, confidenceLabel: "Edited", lineItems: newLines);

        Assert.True(result.IsSuccess);
        Assert.Equal("NewVendor", draft.VendorName);
        Assert.Equal(330m, draft.TotalAmount);
    }

    [Fact]
    public void UpdateDraftFields_AfterSubmit_Fails()
    {
        var draft = NewDraft();
        draft.MarkSubmitted();

        var result = draft.UpdateDraftFields(
            "X", "X", new DateOnly(2026, 5, 2), "SaaS", null,
            200m, null, 0m, 0m, 200m, "X", new[] { Line(1m, 200m, null, 0m, 200m) });

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.AlreadySubmitted.Code, result.Error.Code);
    }

    [Fact]
    public void SoftDelete_Active_DeactivatesAndRaisesEvent()
    {
        var draft = NewDraft();

        var result = draft.SoftDelete();

        Assert.True(result.IsSuccess);
        Assert.False(draft.IsActive);
        Assert.NotNull(draft.DeletedAt);
        Assert.Contains(
            draft.GetDomainEvents(),
            e => e is FinFlow.Domain.Events.UploadedDocumentDraftDeletedDomainEvent);
    }

    [Fact]
    public void SoftDelete_Twice_Fails()
    {
        var draft = NewDraft();
        draft.SoftDelete();

        var result = draft.SoftDelete();

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.AlreadySubmitted.Code, result.Error.Code);
    }
}
