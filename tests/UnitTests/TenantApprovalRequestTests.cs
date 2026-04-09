using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.UnitTests;

public sealed class TenantApprovalRequestTests
{
    [Fact]
    public void Create_Succeeds_WithValidIsolatedRequest()
    {
        var expiresAt = DateTime.UtcNow.AddDays(7);

        var result = TenantApprovalRequest.Create(
            "acme-enterprise",
            "Acme Enterprise",
            "Acme Enterprise Ltd",
            "1234567890",
            "HCM City",
            "0123456789",
            "Alice",
            "Fintech",
            250,
            "vnd",
            Guid.NewGuid(),
            expiresAt);

        Assert.True(result.IsSuccess);
        Assert.Equal("acme-enterprise", result.Value.TenantCode);
        Assert.Equal("VND", result.Value.Currency);
        Assert.Equal(TenantApprovalStatus.Pending, result.Value.Status);
        Assert.Equal(TenancyModel.Isolated, result.Value.TenancyModel);
        Assert.Equal(expiresAt, result.Value.ExpiresAt);
    }

    [Fact]
    public void Create_Fails_WhenCompanyInfoMissing()
    {
        var result = TenantApprovalRequest.Create(
            "acme-enterprise",
            "Acme Enterprise",
            "",
            "",
            null,
            null,
            null,
            null,
            null,
            "VND",
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(7));

        Assert.True(result.IsFailure);
        Assert.Equal(TenantApprovalRequestErrors.CompanyInfoRequired.Code, result.Error.Code);
    }

    [Fact]
    public void Approve_Fails_WhenExpired()
    {
        var request = TenantApprovalRequest.Create(
            "acme-enterprise",
            "Acme Enterprise",
            "Acme Enterprise Ltd",
            "1234567890",
            null,
            null,
            null,
            null,
            null,
            "VND",
            Guid.NewGuid(),
            DateTime.UtcNow.AddSeconds(1)).Value;

        Thread.Sleep(1200);
        var result = request.Approve();

        Assert.True(result.IsFailure);
        Assert.Equal(TenantApprovalRequestErrors.Expired.Code, result.Error.Code);
    }

    [Fact]
    public void Reject_Fails_WhenReasonMissing()
    {
        var request = TenantApprovalRequest.Create(
            "acme-enterprise",
            "Acme Enterprise",
            "Acme Enterprise Ltd",
            "1234567890",
            null,
            null,
            null,
            null,
            null,
            "VND",
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(7)).Value;

        var result = request.Reject(" ");

        Assert.True(result.IsFailure);
        Assert.Equal(TenantApprovalRequestErrors.RejectionReasonRequired.Code, result.Error.Code);
    }

    [Fact]
    public void Reject_SetsRejectedAt_WhenSuccessful()
    {
        var request = TenantApprovalRequest.Create(
            "acme-enterprise",
            "Acme Enterprise",
            "Acme Enterprise Ltd",
            "1234567890",
            null,
            null,
            null,
            null,
            null,
            "VND",
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(7)).Value;

        var result = request.Reject("Need more documents");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantApprovalStatus.Rejected, request.Status);
        Assert.NotNull(request.RejectedAt);
    }
}
