using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.UnitTests;

public sealed class ReviewedDocumentTests
{
    [Fact]
    public void CreateSubmitted_SetsReadyForApprovalStatus()
    {
        var document = CreateDocument();

        Assert.True(document.Status == ReviewedDocumentStatus.ReadyForApproval);
        Assert.Null(document.RejectionReason);
    }

    [Fact]
    public void Approve_TransitionsToApproved_AndClearsReason()
    {
        var document = CreateDocument();

        var result = document.Approve();

        Assert.True(result.IsSuccess);
        Assert.Equal(ReviewedDocumentStatus.Approved, document.Status);
        Assert.Null(document.RejectionReason);
    }

    [Fact]
    public void Approve_Fails_AfterReject()
    {
        var document = CreateDocument();
        document.Reject("Needs more review");

        var result = document.Approve();

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.AlreadyProcessed.Code, result.Error.Code);
    }

    [Fact]
    public void Reject_TransitionsToRejected_AndStoresReason()
    {
        var document = CreateDocument();

        var result = document.Reject("Duplicate invoice submitted");

        Assert.True(result.IsSuccess);
        Assert.Equal(ReviewedDocumentStatus.Rejected, document.Status);
        Assert.Equal("Duplicate invoice submitted", document.RejectionReason);
    }

    [Fact]
    public void Reject_Fails_WhenReasonMissing()
    {
        var document = CreateDocument();

        var result = document.Reject(" ");

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.RejectionReasonRequired.Code, result.Error.Code);
    }

    [Fact]
    public void CreateSubmitted_Fails_When_LineItemQuantity_Is_Not_Positive()
    {
        var result = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "invoice.pdf",
            "application/pdf",
            "Amazon Web Services, Inc.",
            "INV-2026-0101",
            new DateOnly(2026, 4, 18),
            "Software & SaaS",
            "TX-990-2134",
            1200m,
            250m,
            1450m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.SpecifyKind(new DateTime(2026, 4, 18, 9, 0, 0), DateTimeKind.Utc),
            new[]
            {
                ReviewedDocumentLineItem.Create("Cloud Compute Instance - t3.large", -1m, 850m, 850m),
                ReviewedDocumentLineItem.Create("Storage Block (EBS) - 2TB", 1m, 300m, 300m),
                ReviewedDocumentLineItem.Create("Support Plan - Business", 1m, 300m, 300m)
            });

        Assert.True(result.IsFailure);
        Assert.Equal("Line item quantity must be greater than zero.", result.Error.Description);
    }

    [Fact]
    public void CreateSubmitted_Fails_When_LineItems_Do_Not_Match_TotalAmount()
    {
        var result = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "invoice.pdf",
            "application/pdf",
            "Amazon Web Services, Inc.",
            "INV-2026-0101",
            new DateOnly(2026, 4, 18),
            "Software & SaaS",
            "TX-990-2134",
            1200m,
            250m,
            1450m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.SpecifyKind(new DateTime(2026, 4, 18, 9, 0, 0), DateTimeKind.Utc),
            new[]
            {
                ReviewedDocumentLineItem.Create("Cloud Compute Instance - t3.large", 1m, 850m, 850m),
                ReviewedDocumentLineItem.Create("Storage Block (EBS) - 2TB", 1m, 300m, 300m),
                ReviewedDocumentLineItem.Create("Support Plan - Business", 1m, 200m, 200m)
            });

        Assert.True(result.IsFailure);
        Assert.Equal("Line item totals must match the reviewed document total amount.", result.Error.Description);
    }

    private static ReviewedDocument CreateDocument() =>
        ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "invoice.pdf",
            "application/pdf",
            "Amazon Web Services, Inc.",
            "INV-2026-0101",
            new DateOnly(2026, 4, 18),
            "Software & SaaS",
            "TX-990-2134",
            1200m,
            250m,
            1450m,
            "staff-upload",
            "staff@finflow.test",
            "Staff corrected",
            DateTime.SpecifyKind(new DateTime(2026, 4, 18, 9, 0, 0), DateTimeKind.Utc),
            new[]
            {
                ReviewedDocumentLineItem.Create("Cloud Compute Instance - t3.large", 1m, 850m, 850m),
                ReviewedDocumentLineItem.Create("Storage Block (EBS) - 2TB", 1m, 300m, 300m),
                ReviewedDocumentLineItem.Create("Support Plan - Business", 1m, 300m, 300m)
            }).Value;
}


// ─── Additional tests: Discount + Lifecycle (Spec: document-draft-lifecycle-and-discount) ───

public sealed class ReviewedDocumentDiscountAndLifecycleTests
{
    private static ReviewedDocument BuildDoc() =>
        ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "invoice.pdf", "application/pdf",
            "Amazon Web Services, Inc.", "INV-2026-0101",
            new DateOnly(2026, 4, 18),
            "Software & SaaS", "TX-990-2134",
            subtotal: 1200m, vat: 250m, totalAmount: 1450m,
            "staff-upload", "staff@finflow.test", "Staff corrected",
            DateTime.SpecifyKind(new DateTime(2026, 4, 18, 9, 0, 0), DateTimeKind.Utc),
            new[]
            {
                ReviewedDocumentLineItem.Create("Cloud Compute Instance - t3.large", 1m, 850m, 850m),
                ReviewedDocumentLineItem.Create("Storage Block (EBS) - 2TB", 1m, 300m, 300m),
                ReviewedDocumentLineItem.Create("Support Plan - Business", 1m, 300m, 300m)
            }).Value;

    [Fact]
    public void CreateSubmitted_WithDocumentDiscount_PassesUbpFormula()
    {
        // Legacy convention: line items are tax-inclusive ⇒ ΣLine = TotalAmount.
        // subtotal=1200, docPercent=10, docDisc=120, vat=216 → total = 1200-120+216 = 1296.
        // Lines must sum to 1296 → use one big line for simplicity.
        var result = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "invoice.pdf", "application/pdf",
            "Acme", "INV-001",
            new DateOnly(2026, 4, 18),
            "SaaS", "TX-1",
            subtotal: 1200m,
            documentDiscountPercent: 10m,
            documentDiscountAmount: 120m,
            vat: 216m,
            totalAmount: 1296m,
            "staff-upload", "staff@finflow.test", "Staff corrected",
            DateTime.SpecifyKind(new DateTime(2026, 4, 18, 9, 0, 0), DateTimeKind.Utc),
            new[] { ReviewedDocumentLineItem.Create("Bundle", 1m, 1296m, 1296m) });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : "");
        Assert.Equal(120m, result.Value.DocumentDiscountAmount);
        Assert.Equal(10m, result.Value.DocumentDiscountPercent);
    }

    [Fact]
    public void CreateSubmitted_DocumentDiscountMismatch_Fails()
    {
        // Percent=10 nhưng Amount=200 (đúng phải là 120)
        var result = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "invoice.pdf", "application/pdf",
            "Acme", "INV-001",
            new DateOnly(2026, 4, 18),
            "SaaS", "TX-1",
            subtotal: 1200m,
            documentDiscountPercent: 10m,
            documentDiscountAmount: 200m,
            vat: 200m,
            totalAmount: 1200m,
            "staff-upload", "staff@finflow.test", "Staff corrected",
            DateTime.SpecifyKind(new DateTime(2026, 4, 18, 9, 0, 0), DateTimeKind.Utc),
            new[] { ReviewedDocumentLineItem.Create("Bundle", 1m, 1200m, 1200m) });

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.DocumentDiscountMismatch.Code, result.Error.Code);
    }

    [Fact]
    public void CreateSubmitted_DocumentDiscountExceedsSubtotal_Fails()
    {
        var result = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "invoice.pdf", "application/pdf",
            "Acme", "INV-001",
            new DateOnly(2026, 4, 18),
            "SaaS", "TX-1",
            subtotal: 100m,
            documentDiscountPercent: null,
            documentDiscountAmount: 200m, // > subtotal
            vat: 0m,
            totalAmount: 100m,
            "staff-upload", "staff@finflow.test", "Staff corrected",
            DateTime.SpecifyKind(new DateTime(2026, 4, 18, 9, 0, 0), DateTimeKind.Utc),
            new[] { ReviewedDocumentLineItem.Create("Bundle", 1m, 100m, 100m) });

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.DocumentDiscountExceedsSubtotal.Code, result.Error.Code);
    }

    [Fact]
    public void CreateSubmitted_LineDiscount_AdjustsLineTotal()
    {
        // Q=2, UP=100, lineDiscount=20 (10%) → line total = 180; subtotal=180 vat=0 total=180
        var result = ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "invoice.pdf", "application/pdf",
            "Acme", "INV-001",
            new DateOnly(2026, 4, 18),
            "SaaS", "TX-1",
            subtotal: 180m,
            documentDiscountPercent: null,
            documentDiscountAmount: 0m,
            vat: 0m,
            totalAmount: 180m,
            "staff-upload", "staff@finflow.test", "Staff corrected",
            DateTime.SpecifyKind(new DateTime(2026, 4, 18, 9, 0, 0), DateTimeKind.Utc),
            new[] { ReviewedDocumentLineItem.Create("Item", 2m, 100m, 10m, 20m, 180m) });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : "");
        var li = result.Value.LineItems.First();
        Assert.Equal(10m, li.DiscountPercent);
        Assert.Equal(20m, li.DiscountAmount);
        Assert.Equal(180m, li.Total);
    }

    [Fact]
    public void Withdraw_FromReadyForApproval_TransitionsToDraft_AndRaisesEvent()
    {
        var doc = BuildDoc();

        var result = doc.Withdraw();

        Assert.True(result.IsSuccess);
        Assert.Equal(ReviewedDocumentStatus.Draft, doc.Status);
        Assert.Contains(
            doc.GetDomainEvents(),
            e => e is FinFlow.Domain.Events.ReviewedDocumentWithdrawnDomainEvent);
    }

    [Fact]
    public void Withdraw_FromApproved_Fails()
    {
        var doc = BuildDoc();
        doc.Approve();

        var result = doc.Withdraw();

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.CannotWithdraw.Code, result.Error.Code);
    }

    [Fact]
    public void Withdraw_FromRejected_Fails()
    {
        var doc = BuildDoc();
        doc.Reject("Bad data");

        var result = doc.Withdraw();

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.CannotWithdraw.Code, result.Error.Code);
    }

    [Fact]
    public void Resubmit_FromDraft_TransitionsToReadyForApproval()
    {
        var doc = BuildDoc();
        doc.Withdraw();

        var resubmittedAt = DateTime.SpecifyKind(new DateTime(2026, 4, 19, 9, 0, 0), DateTimeKind.Utc);
        var result = doc.Resubmit(resubmittedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReviewedDocumentStatus.ReadyForApproval, doc.Status);
        Assert.Equal(resubmittedAt, doc.SubmittedAt);
    }

    [Fact]
    public void Resubmit_FromReadyForApproval_Fails()
    {
        var doc = BuildDoc();

        var result = doc.Resubmit(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.CannotResubmit.Code, result.Error.Code);
    }

    [Fact]
    public void UpdateForResubmit_Allowed_OnlyInDraft_State()
    {
        var doc = BuildDoc();

        // Should fail in ReadyForApproval
        var fail = doc.UpdateForResubmit(
            "NewVendor", "REF-X", new DateOnly(2026, 5, 1), "SaaS", null,
            subtotal: 1450m, documentDiscountPercent: null, documentDiscountAmount: 0m,
            vat: 0m, totalAmount: 1450m, confidenceLabel: "Edited",
            new[] { ReviewedDocumentLineItem.Create("X", 1m, 1450m, 1450m) });
        Assert.True(fail.IsFailure);

        // After withdraw, should succeed
        doc.Withdraw();
        var ok = doc.UpdateForResubmit(
            "NewVendor", "REF-X", new DateOnly(2026, 5, 1), "SaaS", null,
            subtotal: 1450m, documentDiscountPercent: null, documentDiscountAmount: 0m,
            vat: 0m, totalAmount: 1450m, confidenceLabel: "Edited",
            new[] { ReviewedDocumentLineItem.Create("X", 1m, 1450m, 1450m) });
        Assert.True(ok.IsSuccess);
        Assert.Equal("NewVendor", doc.VendorName);
    }
}
