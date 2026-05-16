using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests.Domain;

public sealed class UploadedDocumentDraftLineItemTests
{
    [Fact]
    public void Create_NoDiscount_BackwardCompatible()
    {
        var result = UploadedDocumentDraftLineItem.Create("Item", 2m, 100m, 200m);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.DiscountPercent);
        Assert.Equal(0m, result.Value.DiscountAmount);
        Assert.Equal(200m, result.Value.Total);
    }

    [Fact]
    public void Create_PercentDiscount_ComputesAmount()
    {
        // Q=2, UP=100, percent=10 → expected amount=20, total=180
        var result = UploadedDocumentDraftLineItem.Create("Item", 2m, 100m, 10m, 20m, 180m);

        Assert.True(result.IsSuccess);
        Assert.Equal(10m, result.Value.DiscountPercent);
        Assert.Equal(20m, result.Value.DiscountAmount);
        Assert.Equal(180m, result.Value.Total);
    }

    [Fact]
    public void Create_AmountOnly_NoPercent()
    {
        var result = UploadedDocumentDraftLineItem.Create("Item", 1m, 100m, null, 30m, 70m);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.DiscountPercent);
        Assert.Equal(30m, result.Value.DiscountAmount);
    }

    [Fact]
    public void Create_PercentAmountMismatch_Fails()
    {
        // Q=2, UP=100, percent=10 (would be 20) but Amount=50 supplied
        var result = UploadedDocumentDraftLineItem.Create("Item", 2m, 100m, 10m, 50m, 150m);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineDiscountMismatch.Code, result.Error.Code);
    }

    [Fact]
    public void Create_PercentOver100_Fails()
    {
        var result = UploadedDocumentDraftLineItem.Create("Item", 1m, 100m, 150m, 100m, 0m);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.DiscountPercentOutOfRange.Code, result.Error.Code);
    }

    [Fact]
    public void Create_NegativeAmount_Fails()
    {
        var result = UploadedDocumentDraftLineItem.Create("Item", 1m, 100m, null, -1m, 101m);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.DiscountAmountInvalid.Code, result.Error.Code);
    }

    [Fact]
    public void Create_TotalMismatch_Fails()
    {
        // Q=2, UP=100, discount=20 → total should be 180; supply 200
        var result = UploadedDocumentDraftLineItem.Create("Item", 2m, 100m, null, 20m, 200m);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemTotalInvalid.Code, result.Error.Code);
    }

    [Fact]
    public void Create_TotalLeqZero_Fails()
    {
        // Q=1, UP=100, discount=100 → total = 0
        var result = UploadedDocumentDraftLineItem.Create("Item", 1m, 100m, null, 100m, 0m);

        Assert.True(result.IsFailure);
        Assert.Equal(UploadedDocumentDraftErrors.LineItemTotalInvalid.Code, result.Error.Code);
    }

    [Fact]
    public void Create_ToleranceBoundary_Accepts()
    {
        // Q=3, UP=33.33 → 99.99; discount=0; total=99.99 should pass
        var result = UploadedDocumentDraftLineItem.Create("Item", 3m, 33.33m, null, 0m, 99.99m);

        Assert.True(result.IsSuccess);
    }
}
