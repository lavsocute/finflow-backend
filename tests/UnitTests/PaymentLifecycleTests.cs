using FinFlow.Domain.Events;
using FinFlow.Domain.Expenses;
using Xunit;

namespace FinFlow.UnitTests;

public class PaymentLifecycleTests
{
    [Fact]
    public void Update_OnPendingPayment_Succeeds_AndRaisesEvent()
    {
        var payment = CreatePayment();
        payment.ClearDomainEvents();
        var actor = Guid.NewGuid();

        var result = payment.Update(PaymentMethod.BankTransfer, "updated note", actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal("updated note", payment.Notes);
        var ev = Assert.IsType<PaymentUpdatedDomainEvent>(payment.GetDomainEvents().Single());
        Assert.Equal(PaymentMethod.Cash, ev.OldMethod);
        Assert.Equal(PaymentMethod.BankTransfer, ev.NewMethod);
    }

    [Fact]
    public void Update_OnConfirmedPayment_Fails()
    {
        var payment = CreatePayment();
        payment.Confirm(Guid.NewGuid(), "TX-1");

        var result = payment.Update(PaymentMethod.BankTransfer, "n", Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.UpdateRequiresPending, result.Error);
    }

    [Fact]
    public void Update_WithEmptyMembership_Fails()
    {
        var payment = CreatePayment();

        var result = payment.Update(PaymentMethod.BankTransfer, null, Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.UpdatedByRequired, result.Error);
    }

    [Fact]
    public void Cancel_OnPendingPayment_Succeeds_AndRaisesEvent()
    {
        var payment = CreatePayment();
        payment.ClearDomainEvents();
        var actor = Guid.NewGuid();

        var result = payment.Cancel("Vendor changed mind", actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("Vendor changed mind", payment.CancellationReason);
        Assert.Equal(actor, payment.CancelledByMembershipId);

        var ev = Assert.IsType<PaymentCancelledDomainEvent>(payment.GetDomainEvents().Single());
        Assert.Equal(actor, ev.CancelledByMembershipId);
    }

    [Fact]
    public void Cancel_OnConfirmedPayment_Fails()
    {
        var payment = CreatePayment();
        payment.Confirm(Guid.NewGuid(), null);

        var result = payment.Cancel("test", Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.CancelRequiresPending, result.Error);
    }

    [Fact]
    public void Cancel_WithEmptyReason_Fails()
    {
        var payment = CreatePayment();

        var result = payment.Cancel(" ", Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.CancellationReasonRequired, result.Error);
    }

    [Fact]
    public void Cancel_WithReasonTooLong_Fails()
    {
        var payment = CreatePayment();

        var result = payment.Cancel(new string('x', 501), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.CancellationReasonTooLong, result.Error);
    }

    [Fact]
    public void InitiateRefund_OnConfirmedPayment_Succeeds_AndRaisesEvent()
    {
        var payment = CreatePayment();
        payment.Confirm(Guid.NewGuid(), null);
        payment.ClearDomainEvents();
        var actor = Guid.NewGuid();

        var result = payment.InitiateRefund(50m, "Wrong amount", actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentRefundStatus.Pending, result.Value.Status);
        Assert.Equal(payment.Id, result.Value.PaymentId);

        var ev = Assert.IsType<PaymentRefundedDomainEvent>(payment.GetDomainEvents().Single());
        Assert.Equal(50m, ev.RefundAmount);
        Assert.Equal(actor, ev.InitiatedByMembershipId);
    }

    [Fact]
    public void InitiateRefund_OnPendingPayment_Fails()
    {
        var payment = CreatePayment();

        var result = payment.InitiateRefund(50m, "test", Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.RefundRequiresConfirmed, result.Error);
    }

    [Fact]
    public void InitiateRefund_AmountExceedingPaymentAmount_Fails()
    {
        var payment = CreatePayment();
        payment.Confirm(Guid.NewGuid(), null);

        var result = payment.InitiateRefund(2000m, "test", Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.RefundAmountInvalid, result.Error);
    }

    [Fact]
    public void InitiateRefund_NegativeAmount_Fails()
    {
        var payment = CreatePayment();
        payment.Confirm(Guid.NewGuid(), null);

        var result = payment.InitiateRefund(-1m, "test", Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.RefundAmountInvalid, result.Error);
    }

    [Fact]
    public void PaymentRefund_Create_WithValidInput_Succeeds()
    {
        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        var refund = PaymentRefund.Create(paymentId, tenantId, 50m, "Wrong amount", actor);

        Assert.True(refund.IsSuccess);
        Assert.Equal(paymentId, refund.Value.PaymentId);
        Assert.Equal(tenantId, refund.Value.IdTenant);
        Assert.Equal(PaymentRefundStatus.Pending, refund.Value.Status);
    }

    [Fact]
    public void PaymentRefund_MarkCompleted_OnPending_Succeeds()
    {
        var refund = PaymentRefund.Create(Guid.NewGuid(), Guid.NewGuid(), 50m, "test", Guid.NewGuid()).Value;

        var result = refund.MarkCompleted();

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentRefundStatus.Completed, refund.Status);
        Assert.NotNull(refund.CompletedAt);
    }

    [Fact]
    public void PaymentRefund_MarkFailed_WithReason_Succeeds()
    {
        var refund = PaymentRefund.Create(Guid.NewGuid(), Guid.NewGuid(), 50m, "test", Guid.NewGuid()).Value;

        var result = refund.MarkFailed("Bank rejected");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentRefundStatus.Failed, refund.Status);
        Assert.Equal("Bank rejected", refund.FailureReason);
    }

    [Fact]
    public void PaymentRefund_Create_NegativeAmount_Fails()
    {
        var refund = PaymentRefund.Create(Guid.NewGuid(), Guid.NewGuid(), -1m, "test", Guid.NewGuid());

        Assert.True(refund.IsFailure);
        Assert.Equal(PaymentErrors.RefundAmountInvalid, refund.Error);
    }

    private static Payment CreatePayment(decimal amount = 1000m)
    {
        return Payment.Create(
            idTenant: Guid.NewGuid(),
            documentId: Guid.NewGuid(),
            idDepartment: Guid.NewGuid(),
            amount: amount,
            currencyCode: "VND",
            exchangeRate: 1m,
            baseCurrencyCode: "VND",
            recordedByMembershipId: Guid.NewGuid(),
            method: PaymentMethod.Cash,
            notes: null).Value;
    }
}
