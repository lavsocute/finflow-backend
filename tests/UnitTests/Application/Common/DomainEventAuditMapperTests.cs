using System.Text.Json;
using FinFlow.Application.Common.Audit;
using FinFlow.Domain.Events;
using FinFlow.Domain.Expenses;
using Xunit;

namespace FinFlow.UnitTests.Application.Common;

public class DomainEventAuditMapperTests
{
    private readonly DomainEventAuditMapper _mapper = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();

    [Fact]
    public void Map_ExpenseRejected_ProducesAuditLog()
    {
        var ev = new ExpenseRejectedDomainEvent(Guid.NewGuid(), _tenantId, Guid.NewGuid(), "Missing receipt");

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("EXPENSE_REJECTED", log!.Action);
        Assert.Equal("Expense", log.EntityType);
        Assert.Equal(ev.ExpenseId.ToString(), log.EntityId);
        Assert.Equal(_tenantId, log.IdTenant);
        Assert.Equal(_accountId, log.IdAccount);
        Assert.NotNull(log.NewValue);

        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal("Rejected", payload.GetProperty("status").GetString());
        Assert.Equal("Missing receipt", payload.GetProperty("reason").GetString());
    }

    [Fact]
    public void Map_ExpenseReopened_ProducesAuditLog()
    {
        var ev = new ExpenseReopenedDomainEvent(Guid.NewGuid(), _tenantId, _accountId, "Was rejected by mistake");

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("EXPENSE_REOPENED", log!.Action);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal("Confirmed", payload.GetProperty("status").GetString());
    }

    [Fact]
    public void Map_PaymentRecorded_ProducesAuditLog()
    {
        var ev = new PaymentRecordedDomainEvent(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(), _accountId,
            1500000m, "VND", PaymentMethod.BankTransfer);

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("PAYMENT_RECORDED", log!.Action);
        Assert.Equal("Payment", log.EntityType);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal(1500000m, payload.GetProperty("amount").GetDecimal());
        Assert.Equal("VND", payload.GetProperty("currency").GetString());
        Assert.Equal("BankTransfer", payload.GetProperty("method").GetString());
    }

    [Fact]
    public void Map_PaymentConfirmed_ProducesAuditLog()
    {
        var ev = new PaymentConfirmedDomainEvent(
            Guid.NewGuid(), _tenantId, _accountId, "TX-12345", 1500000m, "VND");

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("PAYMENT_CONFIRMED", log!.Action);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal("Confirmed", payload.GetProperty("status").GetString());
        Assert.Equal("TX-12345", payload.GetProperty("executionReference").GetString());
    }

    [Fact]
    public void Map_PaymentRejected_ProducesAuditLog()
    {
        var ev = new PaymentRejectedDomainEvent(
            Guid.NewGuid(), _tenantId, _accountId, PaymentRejectType.PolicyViolation, "Outside policy");

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("PAYMENT_REJECTED", log!.Action);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal("PolicyViolation", payload.GetProperty("rejectionType").GetString());
    }

    [Fact]
    public void Map_PaymentUpdated_ProducesAuditLog_WithOldAndNewPayload()
    {
        var ev = new PaymentUpdatedDomainEvent(
            Guid.NewGuid(), _tenantId, _accountId,
            PaymentMethod.Cash, PaymentMethod.BankTransfer,
            "old notes", "new notes");

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("PAYMENT_UPDATED", log!.Action);
        Assert.NotNull(log.OldValue);
        Assert.NotNull(log.NewValue);

        var oldPayload = JsonDocument.Parse(log.OldValue!).RootElement;
        var newPayload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal("Cash", oldPayload.GetProperty("method").GetString());
        Assert.Equal("BankTransfer", newPayload.GetProperty("method").GetString());
    }

    [Fact]
    public void Map_PaymentCancelled_ProducesAuditLog()
    {
        var ev = new PaymentCancelledDomainEvent(Guid.NewGuid(), _tenantId, _accountId, "Vendor changed");

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("PAYMENT_CANCELLED", log!.Action);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal("Cancelled", payload.GetProperty("status").GetString());
    }

    [Fact]
    public void Map_PaymentRefunded_ProducesAuditLog()
    {
        var ev = new PaymentRefundedDomainEvent(Guid.NewGuid(), _tenantId, _accountId, 500000m, "Wrong amount");

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("PAYMENT_REFUNDED", log!.Action);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal(500000m, payload.GetProperty("refundAmount").GetDecimal());
    }

    [Fact]
    public void Map_BudgetCreated_ProducesAuditLog()
    {
        var ev = new BudgetCreatedDomainEvent(Guid.NewGuid(), _tenantId, Guid.NewGuid(), 5, 2026, 10_000_000m);

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("BUDGET_CREATED", log!.Action);
        Assert.Equal("Budget", log.EntityType);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal(2026, payload.GetProperty("year").GetInt32());
        Assert.Equal(10_000_000m, payload.GetProperty("allocatedAmount").GetDecimal());
    }

    [Fact]
    public void Map_BudgetUpdated_ProducesAuditLog()
    {
        var ev = new BudgetUpdatedDomainEvent(Guid.NewGuid(), _tenantId, Guid.NewGuid(), 10_000_000m, 4_500_000m);

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("BUDGET_UPDATED", log!.Action);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal(4_500_000m, payload.GetProperty("spentAmount").GetDecimal());
    }

    [Fact]
    public void Map_DocumentApproved_ProducesAuditLog()
    {
        var ev = new ReviewedDocumentApprovedDomainEvent(Guid.NewGuid(), _tenantId, _accountId);

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("DOCUMENT_APPROVED", log!.Action);
        Assert.Equal("ReviewedDocument", log.EntityType);
    }

    [Fact]
    public void Map_DocumentRejected_ProducesAuditLog()
    {
        var ev = new ReviewedDocumentRejectedDomainEvent(Guid.NewGuid(), _tenantId, _accountId, "Bad receipt");

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.NotNull(log);
        Assert.Equal("DOCUMENT_REJECTED", log!.Action);
        var payload = JsonDocument.Parse(log.NewValue!).RootElement;
        Assert.Equal("Bad receipt", payload.GetProperty("reason").GetString());
    }

    [Fact]
    public void Map_UnknownEvent_ReturnsNull()
    {
        var ev = new UnknownTestEvent();

        var log = _mapper.Map(ev, _tenantId, _accountId);

        Assert.Null(log);
    }

    private sealed record UnknownTestEvent : FinFlow.Domain.Abstractions.IDomainEvent
    {
        public DateTime OccurredOn => DateTime.UtcNow;
    }
}
