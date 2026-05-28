using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests.Domain;

public sealed class ReviewedDocumentLineVatTests
{
    [Fact]
    public void CreateSubmitted_WithLineVatGroupedByRate_Succeeds()
    {
        var document = CreateReviewedDocument(
            [
                ReviewedDocumentLineItem.Create("Fresh food", 1m, 100000m, null, 0m, 100000m, 5m, 100000m, 5000m),
                ReviewedDocumentLineItem.Create("Household item", 1m, 200000m, null, 0m, 200000m, 10m, 200000m, 20000m)
            ],
            [
                ReviewedDocumentTaxLine.Create("VAT", 5m, 100000m, 5000m).Value,
                ReviewedDocumentTaxLine.Create("VAT", 10m, 200000m, 20000m).Value
            ]);

        Assert.True(document.IsSuccess, document.Error.Description);
    }

    [Fact]
    public void CreateSubmitted_WithMismatchedGroupedLineVat_Fails()
    {
        var document = CreateReviewedDocument(
            [
                ReviewedDocumentLineItem.Create("Fresh food", 1m, 100000m, null, 0m, 100000m, 5m, 100000m, 5000m),
                ReviewedDocumentLineItem.Create("Household item", 1m, 200000m, null, 0m, 200000m, 10m, 200000m, 20000m)
            ],
            [
                ReviewedDocumentTaxLine.Create("VAT", 5m, 100000m, 5000m).Value,
                ReviewedDocumentTaxLine.Create("VAT", 10m, 200000m, 19000m).Value
            ]);

        Assert.True(document.IsFailure);
        Assert.Equal(ReviewedDocumentErrors.FinancialBreakdownMismatch.Code, document.Error.Code);
    }

    private static FinFlow.Domain.Abstractions.Result<ReviewedDocument> CreateReviewedDocument(
        IReadOnlyCollection<ReviewedDocumentLineItem> lineItems,
        IReadOnlyCollection<ReviewedDocumentTaxLine> taxLines) =>
        ReviewedDocument.CreateSubmitted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "coop-receipt.jpg",
            "image/jpeg",
            "Co.op Mart",
            "COOP-2026-0001",
            new DateOnly(2026, 5, 27),
            "Groceries",
            "0312345678",
            300000m,
            25000m,
            325000m,
            "OCR",
            "reviewer@example.com",
            "Staff corrected",
            DateTime.UtcNow,
            lineItems,
            taxLines);
}
