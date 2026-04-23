using System.Reflection;
using FinFlow.Domain.Abstractions;
namespace FinFlow.UnitTests.Domain;

public sealed class EmailChallengeTests
{
    [Fact]
    public void Create_UsableChallenge_StartsPendingAndCanResend()
    {
        var nowUtc = new DateTime(2026, 4, 13, 2, 30, 0, DateTimeKind.Utc);
        var challenge = CreateChallenge("VerifyEmail", nowUtc, nowUtc.AddMinutes(10));

        var isUsableMethod = challenge.GetType().GetMethod("IsUsableAt", BindingFlags.Public | BindingFlags.Instance);
        var canResendMethod = challenge.GetType().GetMethod("CanResendAt", BindingFlags.Public | BindingFlags.Instance);
        var otpFailedAttemptCountProp = challenge.GetType().GetProperty("OtpFailedAttemptCount", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(isUsableMethod);
        Assert.NotNull(canResendMethod);
        Assert.NotNull(otpFailedAttemptCountProp);

        Assert.True((bool)isUsableMethod!.Invoke(challenge, [nowUtc])!);
        Assert.True((bool)canResendMethod!.Invoke(challenge, [nowUtc])!);
        Assert.Equal(0, (int)otpFailedAttemptCountProp!.GetValue(challenge)!);
    }

    [Fact]
    public void RegisterFailedOtpAttempt_RevokesChallenge_AfterMaxAttempts()
    {
        var nowUtc = new DateTime(2026, 4, 13, 2, 30, 0, DateTimeKind.Utc);
        var challenge = CreateChallenge("ResetPassword", nowUtc, nowUtc.AddMinutes(10));
        var method = challenge.GetType().GetMethod("RegisterFailedOtpAttempt", BindingFlags.Public | BindingFlags.Instance);
        var isRevokedProp = challenge.GetType().GetProperty("IsRevoked", BindingFlags.Public | BindingFlags.Instance);
        var isUsableMethod = challenge.GetType().GetMethod("IsUsableAt", BindingFlags.Public | BindingFlags.Instance);
        var otpFailedAttemptCountProp = challenge.GetType().GetProperty("OtpFailedAttemptCount", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.NotNull(isRevokedProp);
        Assert.NotNull(isUsableMethod);
        Assert.NotNull(otpFailedAttemptCountProp);

        Result? lastResult = null;
        for (var i = 0; i < 5; i++)
        {
            lastResult = (Result)method!.Invoke(challenge, [nowUtc.AddSeconds(i)])!;
        }

        Assert.NotNull(lastResult);
        Assert.True(lastResult!.IsSuccess);
        Assert.True((bool)isRevokedProp!.GetValue(challenge)!);
        Assert.False((bool)isUsableMethod!.Invoke(challenge, [nowUtc.AddSeconds(4)])!);
        Assert.Equal(5, (int)otpFailedAttemptCountProp!.GetValue(challenge)!);
    }

    [Fact]
    public void Consume_MakesChallenge_Unusable_ForSecondUse()
    {
        var nowUtc = new DateTime(2026, 4, 13, 2, 30, 0, DateTimeKind.Utc);
        var challenge = CreateChallenge("VerifyEmail", nowUtc, nowUtc.AddMinutes(10));
        var consumeMethod = challenge.GetType().GetMethod("Consume", BindingFlags.Public | BindingFlags.Instance);
        var isUsableMethod = challenge.GetType().GetMethod("IsUsableAt", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(consumeMethod);
        Assert.NotNull(isUsableMethod);

        var firstResult = (Result)consumeMethod!.Invoke(challenge, [nowUtc])!;
        var secondResult = (Result)consumeMethod.Invoke(challenge, [nowUtc.AddMinutes(1)])!;

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsFailure);
        Assert.False((bool)isUsableMethod!.Invoke(challenge, [nowUtc.AddMinutes(1)])!);
    }

    private static object CreateChallenge(string purposeName, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        var challengeType = Type.GetType("FinFlow.Domain.Entities.EmailChallenge, FinFlow.Domain");

        Assert.NotNull(challengeType);

        var purposeType = Type.GetType("FinFlow.Domain.Enums.EmailChallengePurpose, FinFlow.Domain");

        Assert.NotNull(purposeType);

        var purpose = Enum.Parse(purposeType!, purposeName);

        var createMethod = challengeType!.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(createMethod);

        var result = createMethod!.Invoke(null, [
            Guid.NewGuid(),
            purpose,
            createdAtUtc,
            expiresAtUtc,
            "",
            "",
            null,
            null,
            5,
            0
        ]);

        Assert.NotNull(result);

        var valueProperty = result.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(valueProperty);

        return valueProperty!.GetValue(result)!;
    }
}
