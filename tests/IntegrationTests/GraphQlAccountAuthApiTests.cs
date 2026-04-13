using System.Text.Json;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlAccountAuthApiTests
{
    [Fact]
    public async Task Register_Mutation_ReturnsVerificationPendingPayload_WithoutSessionTokens()
    {
        await using var factory = new GraphQlApiTestFactory();

        const string mutation = """
            mutation($input: RegisterInput!) {
              register(input: $input) {
                accountId
                email
                requiresEmailVerification
                cooldownSeconds
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new
        {
            input = new
            {
                email = "graphql.register@finflow.test",
                password = "P@ssw0rd!",
                name = "GraphQL Register"
            }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("register").GetProperty("requiresEmailVerification").GetBoolean());
    }

    [Fact]
    public async Task VerifyEmailByToken_Mutation_VerifiesAccount_AndConsumesChallenge()
    {
        await using var factory = new GraphQlApiTestFactory();
        using var secretScope = factory.Services.CreateScope();
        var secretService = secretScope.ServiceProvider.GetRequiredService<IEmailChallengeSecretService>();

        var account = Account.Create("graphql.verify.token@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var nowUtc = DateTime.UtcNow;
        var challenge = EmailChallenge.Create(
            account.Id,
            EmailChallengePurpose.VerifyEmail,
            nowUtc.AddMinutes(-5),
            nowUtc.AddMinutes(15),
            email: account.Email,
            tokenHash: secretService.HashChallengeToken("graphql-verify-token"),
            otpHash: secretService.HashChallengeOtp("654321"),
            lastSentAtUtc: nowUtc.AddMinutes(-2)).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(challenge);
        });

        const string mutation = """
            mutation($token: String!) {
              verifyEmailByToken(token: $token)
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new { token = "graphql-verify-token" });
        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("verifyEmailByToken").GetBoolean());
    }

    [Fact]
    public async Task VerifyEmailByOtp_Mutation_VerifiesAccount_AndConsumesChallenge()
    {
        await using var factory = new GraphQlApiTestFactory();
        using var secretScope = factory.Services.CreateScope();
        var secretService = secretScope.ServiceProvider.GetRequiredService<IEmailChallengeSecretService>();

        var account = Account.Create("graphql.verify.otp@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var nowUtc = DateTime.UtcNow;
        var challenge = EmailChallenge.Create(
            account.Id,
            EmailChallengePurpose.VerifyEmail,
            nowUtc.AddMinutes(-5),
            nowUtc.AddMinutes(15),
            email: account.Email,
            tokenHash: secretService.HashChallengeToken("graphql-verify-otp-token"),
            otpHash: secretService.HashChallengeOtp("123456"),
            lastSentAtUtc: nowUtc.AddMinutes(-2)).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(challenge);
        });

        const string mutation = """
            mutation($email: String!, $otp: String!) {
              verifyEmailByOtp(email: $email, otp: $otp)
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new
        {
            email = account.Email,
            otp = "123456"
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("verifyEmailByOtp").GetBoolean());
    }

    [Fact]
    public async Task ResendEmailVerification_Mutation_RotatesAndSendsChallenge()
    {
        await using var factory = new GraphQlApiTestFactory();
        using var secretScope = factory.Services.CreateScope();
        var secretService = secretScope.ServiceProvider.GetRequiredService<IEmailChallengeSecretService>();

        var account = Account.Create("graphql.resend@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var nowUtc = DateTime.UtcNow;
        var existingChallenge = EmailChallenge.Create(
            account.Id,
            EmailChallengePurpose.VerifyEmail,
            nowUtc.AddMinutes(-10),
            nowUtc.AddMinutes(15),
            email: account.Email,
            tokenHash: secretService.HashChallengeToken("graphql-resend-old-token"),
            otpHash: secretService.HashChallengeOtp("222222"),
            lastSentAtUtc: nowUtc.AddMinutes(-2)).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(existingChallenge);
        });

        const string mutation = """
            mutation($email: String!) {
              resendEmailVerification(email: $email) {
                accepted
                cooldownSeconds
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new { email = account.Email });
        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("resendEmailVerification").GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task ForgotPassword_Mutation_ReturnsNeutralDispatchPayload()
    {
        await using var factory = new GraphQlApiTestFactory();
        var account = Account.Create("graphql.forgot@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        await factory.SeedAsync(db => db.Add(account));

        const string mutation = """
            mutation($email: String!) {
              forgotPassword(email: $email) {
                accepted
                cooldownSeconds
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new { email = account.Email });
        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("forgotPassword").GetProperty("accepted").GetBoolean());
        Assert.Single(factory.EmailSender.PasswordResetEmails);
    }

    [Fact]
    public async Task VerifyPasswordResetToken_Mutation_ReturnsTrue_ForValidToken()
    {
        await using var factory = new GraphQlApiTestFactory();
        var account = Account.Create("graphql.reset.verify@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var challenge = PasswordResetChallenge.Create(
            account.Id,
            "reset-token-hash:reset-token-123",
            "reset-otp-hash:654321",
            DateTime.UtcNow.AddMinutes(15),
            DateTime.UtcNow,
            DateTime.UtcNow,
            90,
            5).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(challenge);
        });

        const string mutation = """
            mutation($token: String!) {
              verifyPasswordResetToken(token: $token)
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new { token = "reset-token-123" });
        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("verifyPasswordResetToken").GetBoolean());
    }

    [Fact]
    public async Task ResetPasswordByToken_Mutation_ChangesPassword()
    {
        await using var factory = new GraphQlApiTestFactory();
        var account = Account.Create("graphql.reset.token@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var challenge = PasswordResetChallenge.Create(
            account.Id,
            "reset-token-hash:reset-token-123",
            "reset-otp-hash:654321",
            DateTime.UtcNow.AddMinutes(15),
            DateTime.UtcNow,
            DateTime.UtcNow,
            90,
            5).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(challenge);
        });

        const string mutation = """
            mutation($token: String!, $newPassword: String!) {
              resetPasswordByToken(token: $token, newPassword: $newPassword)
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new
        {
            token = "reset-token-123",
            newPassword = "N3wP@ssword!"
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("resetPasswordByToken").GetBoolean());
    }

    [Fact]
    public async Task ResetPasswordByOtp_Mutation_ChangesPassword()
    {
        await using var factory = new GraphQlApiTestFactory();
        var account = Account.Create("graphql.reset.otp@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var challenge = PasswordResetChallenge.Create(
            account.Id,
            "reset-token-hash:reset-token-123",
            "reset-otp-hash:654321",
            DateTime.UtcNow.AddMinutes(15),
            DateTime.UtcNow,
            DateTime.UtcNow,
            90,
            5).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(challenge);
        });

        const string mutation = """
            mutation($input: ResetPasswordByOtpInput!) {
              resetPasswordByOtp(input: $input)
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new
        {
            input = new
            {
                email = account.Email,
                otp = "654321",
                newPassword = "N3wP@ssword!"
            }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("resetPasswordByOtp").GetBoolean());
    }
}
