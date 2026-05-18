using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using Xunit;

namespace FinFlow.UnitTests.Domain.Budgets;

public class BudgetTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Dept = Guid.NewGuid();

    [Fact]
    public void Create_HappyPath_ReturnsActiveBudget_WithSnapshotCurrency_AndDefaultMode()
    {
        var result = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, baseCurrencyCode: "vnd");

        Assert.True(result.IsSuccess);
        var b = result.Value;
        Assert.Equal("VND", b.BaseCurrencyCode);
        Assert.Equal(BudgetEnforcementMode.SoftBlock, b.EnforcementMode);
        Assert.True(b.IsActive);
        Assert.Equal(0m, b.CommittedAmount);
        Assert.Equal(0m, b.SpentAmount);
        Assert.Equal(1_000_000m, b.AvailableAmount);
    }

    [Fact]
    public void Create_RejectsBlankCurrency()
    {
        var result = Budget.Create(Tenant, Dept, 5, 2026, 100m, baseCurrencyCode: "");
        Assert.True(result.IsFailure);
        Assert.Equal(BudgetErrors.CurrencyRequired, result.Error);
    }

    [Fact]
    public void ApplyCommitment_UpdatesCommittedOnly_AndAvailableShrinks()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND").Value;

        var r = b.ApplyCommitment(300_000m, BudgetExceededTrigger.ApproveDocument);

        Assert.True(r.IsSuccess);
        Assert.Equal(300_000m, b.CommittedAmount);
        Assert.Equal(0m, b.SpentAmount);
        Assert.Equal(700_000m, b.AvailableAmount);
    }

    [Fact]
    public void ReleaseCommitment_RestoresAvailable_AndFloorsAtZero()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND").Value;
        b.ApplyCommitment(300_000m, BudgetExceededTrigger.ApproveDocument);

        var r = b.ReleaseCommitment(300_000m);

        Assert.True(r.IsSuccess);
        Assert.Equal(0m, b.CommittedAmount);
        Assert.Equal(1_000_000m, b.AvailableAmount);
    }

    [Fact]
    public void ReleaseCommitment_RejectsOverRelease()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND").Value;
        b.ApplyCommitment(100m, BudgetExceededTrigger.ApproveDocument);

        var r = b.ReleaseCommitment(200m);

        Assert.True(r.IsFailure);
        Assert.Equal(BudgetErrors.InsufficientCommitment, r.Error);
    }

    [Fact]
    public void ApplyConfirmation_MovesCommittedToSpent_NoNetAvailableChange()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND").Value;
        b.ApplyCommitment(500_000m, BudgetExceededTrigger.ApproveDocument);

        var r = b.ApplyConfirmation(300_000m, BudgetExceededTrigger.ConfirmPayment);

        Assert.True(r.IsSuccess);
        Assert.Equal(200_000m, b.CommittedAmount);
        Assert.Equal(300_000m, b.SpentAmount);
        Assert.Equal(500_000m, b.AvailableAmount);   // unchanged: same total reservation
    }

    [Fact]
    public void ApplyConfirmation_RejectsIfAmountExceedsCommitted()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND").Value;
        b.ApplyCommitment(100m, BudgetExceededTrigger.ApproveDocument);

        var r = b.ApplyConfirmation(200m, BudgetExceededTrigger.ConfirmPayment);

        Assert.True(r.IsFailure);
        Assert.Equal(BudgetErrors.InsufficientCommitment, r.Error);
    }

    [Fact]
    public void ApplyCommitment_CrossingLowThreshold_Raises85PercentWarning()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND").Value;
        ClearEvents(b);

        b.ApplyCommitment(900_000m, BudgetExceededTrigger.ApproveDocument);   // 90%

        var warnings = b.GetDomainEvents()
            .OfType<BudgetWarningThresholdReachedDomainEvent>()
            .ToList();
        Assert.Contains(warnings, w => w.Threshold == 85m);
    }

    [Fact]
    public void ApplyCommitment_CrossingOverBudget_RaisesBudgetExceededEvent()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND").Value;
        ClearEvents(b);

        b.ApplyCommitment(1_500_000m, BudgetExceededTrigger.ApproveDocument);

        var exceeded = b.GetDomainEvents()
            .OfType<BudgetExceededDomainEvent>()
            .ToList();
        Assert.Single(exceeded);
        Assert.Equal(500_000m, exceeded[0].OverAmount);
    }

    [Fact]
    public void Available_IncludesCarryOver()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND",
            carryOverFromPreviousMonth: 300_000m).Value;

        Assert.Equal(1_300_000m, b.AvailableAmount);
    }

    [Fact]
    public void ChangeEnforcementMode_TogglesValueAndIsIdempotent()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1_000_000m, "VND").Value;
        Assert.Equal(BudgetEnforcementMode.SoftBlock, b.EnforcementMode);

        var first = b.ChangeEnforcementMode(BudgetEnforcementMode.HardBlock);
        var second = b.ChangeEnforcementMode(BudgetEnforcementMode.HardBlock);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);   // idempotent
        Assert.Equal(BudgetEnforcementMode.HardBlock, b.EnforcementMode);
    }

    [Fact]
    public void Archive_FlipsIsActiveOnce()
    {
        var b = Budget.Create(Tenant, Dept, 5, 2026, 1m, "VND").Value;
        var first = b.Archive();
        var second = b.Archive();

        Assert.True(first.IsSuccess);
        Assert.False(b.IsActive);
        Assert.True(second.IsFailure);
        Assert.Equal(BudgetErrors.AlreadyArchived, second.Error);
    }

    private static void ClearEvents(Budget b) => b.ClearDomainEvents();
}
