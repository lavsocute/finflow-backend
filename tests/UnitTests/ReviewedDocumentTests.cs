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
            "invoice.pdf",
            "application/pdf",
            "Amazon Web Services, Inc.",
            "INV-2026-0101",
            new DateOnly(2026, 4, 18),
            new DateOnly(2026, 5, 2),
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
            "invoice.pdf",
            "application/pdf",
            "Amazon Web Services, Inc.",
            "INV-2026-0101",
            new DateOnly(2026, 4, 18),
            new DateOnly(2026, 5, 2),
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
            "invoice.pdf",
            "application/pdf",
            "Amazon Web Services, Inc.",
            "INV-2026-0101",
            new DateOnly(2026, 4, 18),
            new DateOnly(2026, 5, 2),
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
