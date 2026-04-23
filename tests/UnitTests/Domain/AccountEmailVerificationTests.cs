using System.Reflection;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests.Domain;

public sealed class AccountEmailVerificationTests
{
    [Fact]
    public void Create_Defaults_NewAccount_To_Unverified()
    {
        var account = Account.Create("user@finflow.test", "password-hash").Value;

        var isVerifiedProperty = typeof(Account).GetProperty("IsEmailVerified", BindingFlags.Public | BindingFlags.Instance);
        var verifiedAtProperty = typeof(Account).GetProperty("EmailVerifiedAt", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(isVerifiedProperty);
        Assert.NotNull(verifiedAtProperty);
        Assert.False((bool)isVerifiedProperty!.GetValue(account)!);
        Assert.Null(verifiedAtProperty!.GetValue(account));
    }

    [Fact]
    public void MarkEmailVerified_SetsVerificationState_AndTimestamp()
    {
        var createdAtUtc = new DateTime(2026, 4, 13, 1, 0, 0, DateTimeKind.Utc);
        var account = Account.Create("user@finflow.test", "password-hash", createdAtUtc).Value;
        var verifiedAtUtc = new DateTime(2026, 4, 13, 2, 30, 0, DateTimeKind.Utc);

        var method = typeof(Account).GetMethod("MarkEmailVerified", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);

        var result = (Result)method!.Invoke(account, [verifiedAtUtc])!;

        Assert.True(result.IsSuccess);
        Assert.True((bool)typeof(Account).GetProperty("IsEmailVerified")!.GetValue(account)!);
        Assert.Equal(verifiedAtUtc, typeof(Account).GetProperty("EmailVerifiedAt")!.GetValue(account));
    }

    [Fact]
    public void MarkEmailVerified_Fails_WhenAlreadyVerified()
    {
        var createdAtUtc = new DateTime(2026, 4, 13, 1, 0, 0, DateTimeKind.Utc);
        var account = Account.Create("user@finflow.test", "password-hash", createdAtUtc).Value;
        var method = typeof(Account).GetMethod("MarkEmailVerified", BindingFlags.Public | BindingFlags.Instance)!;

        var firstResult = (Result)method.Invoke(account, [new DateTime(2026, 4, 13, 2, 30, 0, DateTimeKind.Utc)])!;
        var secondResult = (Result)method.Invoke(account, [new DateTime(2026, 4, 13, 2, 31, 0, DateTimeKind.Utc)])!;

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsFailure);
        Assert.Equal("Account.EmailAlreadyVerified", secondResult.Error.Code);
    }
}
