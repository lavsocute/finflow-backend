using FinFlow.Application.Auth.Commands.ChangePassword;
using FinFlow.Application.Auth.Commands.ForgotPassword;
using FinFlow.Application.Auth.Commands.Login;
using FinFlow.Application.Auth.Commands.Logout;
using FinFlow.Application.Auth.Commands.RefreshToken;
using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application.Auth.Commands.ResendEmailVerification;
using FinFlow.Application.Auth.Commands.ResetPasswordByOtp;
using FinFlow.Application.Auth.Commands.ResetPasswordByToken;
using FinFlow.Application.Auth.Commands.VerifyEmailByOtp;
using FinFlow.Application.Auth.Commands.VerifyEmailByToken;
using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests;

public sealed class AuthCommandHandlerIntegrationTests
{
    private readonly AuthFlowTestFixture _fixture = new();

    [Fact]
    public async Task LoginCommandHandler_ReturnsAccountSession_ForValidCredentialsWithoutWorkspaceState()
    {
        using var scope = _fixture.CreateScope();
        var account = scope.SeedAccount("handler.login@finflow.test", "P@ssw0rd!");
        account.MarkEmailVerified(scope.Clock.UtcNow);
        await scope.SaveSeedAsync();

        var result = await scope.CreateLoginHandler().Handle(
            new LoginCommand(new LoginRequest(account.Email, "P@ssw0rd!", "127.0.0.1")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(account.Email, result.Value.Email);
        Assert.Equal("account", result.Value.SessionKind);
    }

    [Fact]
    public async Task RegisterCommandHandler_ReturnsVerificationPendingPayload_AndSendsVerificationEmail()
    {
        using var scope = _fixture.CreateScope();

        var result = await scope.CreateRegisterHandler().Handle(
            new RegisterCommand(new RegisterRequest("handler.register@finflow.test", "P@ssw0rd!", "FinFlow Team", "127.0.0.1")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RequiresEmailVerification);
        Assert.Single(scope.EmailSender.VerificationEmails);
    }

    [Fact]
    public async Task VerifyEmailByTokenCommandHandler_MarksAccountVerified_AndConsumesChallenge()
    {
        using var scope = _fixture.CreateScope();
        var account = scope.SeedAccount("handler.verify.token@finflow.test", "P@ssw0rd!");
        var nowUtc = scope.Clock.UtcNow;
        var challenge = EmailChallenge.Create(
            account.Id,
            EmailChallengePurpose.VerifyEmail,
            nowUtc.AddMinutes(-5),
            nowUtc.AddMinutes(10),
            email: account.Email,
            tokenHash: scope.SecretService.HashChallengeToken("verify-token-123"),
            otpHash: scope.SecretService.HashChallengeOtp("123456"),
            lastSentAtUtc: nowUtc.AddMinutes(-1)).Value;
        scope.DbContext.Add(challenge);
        await scope.SaveSeedAsync();

        var result = await scope.CreateVerifyEmailByTokenHandler().Handle(
            new VerifyEmailByTokenCommand(new VerifyEmailByTokenRequest("verify-token-123")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updatedChallenge = await scope.DbContext.Set<EmailChallenge>().IgnoreQueryFilters().SingleAsync(x => x.Id == challenge.Id);
        Assert.True(updatedChallenge.IsConsumed);
    }

    [Fact]
    public async Task ResendEmailVerificationCommandHandler_RotatesChallenge_AndSendsFreshVerificationEmail()
    {
        using var scope = _fixture.CreateScope();
        var account = scope.SeedAccount("handler.resend@finflow.test", "P@ssw0rd!");
        var nowUtc = scope.Clock.UtcNow;
        var existingChallenge = EmailChallenge.Create(
            account.Id,
            EmailChallengePurpose.VerifyEmail,
            nowUtc.AddMinutes(-10),
            nowUtc.AddMinutes(10),
            email: account.Email,
            tokenHash: scope.SecretService.HashChallengeToken("resend-old-token"),
            otpHash: scope.SecretService.HashChallengeOtp("111111"),
            lastSentAtUtc: nowUtc.AddMinutes(-2)).Value;
        scope.DbContext.Add(existingChallenge);
        await scope.SaveSeedAsync();

        var result = await scope.CreateResendEmailVerificationHandler().Handle(
            new ResendEmailVerificationCommand(new ResendEmailVerificationRequest(account.Email)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(scope.EmailSender.VerificationEmails);
    }

    [Fact]
    public async Task ForgotPasswordCommandHandler_ReturnsNeutralResponse_AndCreatesChallenge()
    {
        using var scope = _fixture.CreateScope();
        var account = scope.SeedAccount("handler.forgot@finflow.test", "P@ssw0rd!");
        await scope.SaveSeedAsync();

        var result = await scope.CreateForgotPasswordHandler().Handle(
            new ForgotPasswordCommand(new ForgotPasswordRequest(account.Email)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Accepted);
        Assert.Single(scope.EmailSender.PasswordResetEmails);
    }

    [Fact]
    public async Task ResetPasswordByTokenCommandHandler_ChangesPassword_AndRevokesTokens()
    {
        using var scope = _fixture.CreateScope();
        var account = scope.SeedAccount("handler.reset.token@finflow.test", "P@ssw0rd!");
        scope.SeedAccountRefreshToken("handler-reset-token-1", account.Id);
        var challenge = PasswordResetChallenge.Create(
            account.Id,
            scope.PasswordResetSecretService.HashToken("reset-token-123"),
            scope.PasswordResetSecretService.HashOtp("654321"),
            DateTime.UtcNow.AddMinutes(15),
            DateTime.UtcNow,
            DateTime.UtcNow,
            90,
            5).Value;
        scope.DbContext.Add(challenge);
        await scope.SaveSeedAsync();

        var result = await scope.CreateResetPasswordByTokenHandler().Handle(
            new ResetPasswordByTokenCommand(new ResetPasswordByTokenRequest("reset-token-123", "N3wP@ssword!")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updatedAccount = await scope.DbContext.Set<Account>().IgnoreQueryFilters().SingleAsync(x => x.Id == account.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("N3wP@ssword!", updatedAccount.PasswordHash));
    }

    [Fact]
    public async Task ResetPasswordByOtpCommandHandler_ChangesPassword_AndConsumesChallenge()
    {
        using var scope = _fixture.CreateScope();
        var account = scope.SeedAccount("handler.reset.otp@finflow.test", "P@ssw0rd!");
        var challenge = PasswordResetChallenge.Create(
            account.Id,
            scope.PasswordResetSecretService.HashToken("reset-token-123"),
            scope.PasswordResetSecretService.HashOtp("654321"),
            DateTime.UtcNow.AddMinutes(15),
            DateTime.UtcNow,
            DateTime.UtcNow,
            90,
            5).Value;
        scope.DbContext.Add(challenge);
        await scope.SaveSeedAsync();

        var result = await scope.CreateResetPasswordByOtpHandler().Handle(
            new ResetPasswordByOtpCommand(new ResetPasswordByOtpRequest(account.Email, "654321", "N3wP@ssword!")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RefreshTokenCommandHandler_RotatesToken_ForActiveAccount()
    {
        using var scope = _fixture.CreateScope();
        var tenant = scope.SeedTenant("Workspace", "handler-refresh");
        var account = scope.SeedAccount("handler.refresh@finflow.test", "P@ssw0rd!");
        account.MarkEmailVerified(scope.Clock.UtcNow);
        var membership = scope.SeedMembership(account.Id, tenant.Id, RoleType.TenantAdmin);
        scope.SeedRefreshToken("handler-refresh-token", account.Id, membership.Id);
        await scope.SaveSeedAsync();

        var result = await scope.CreateRefreshTokenHandler().Handle(
            new RefreshTokenCommand(new RefreshTokenRequest("handler-refresh-token")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ChangePasswordCommandHandler_RevokesAllRefreshTokens()
    {
        using var scope = _fixture.CreateScope();
        var tenant = scope.SeedTenant("Workspace", "handler-change-password");
        var account = scope.SeedAccount("handler.password@finflow.test", "P@ssw0rd!");
        account.MarkEmailVerified(scope.Clock.UtcNow);
        var membership = scope.SeedMembership(account.Id, tenant.Id, RoleType.TenantAdmin);
        scope.SeedRefreshToken("handler-password-token-1", account.Id, membership.Id);
        scope.SeedRefreshToken("handler-password-token-2", account.Id, membership.Id);
        await scope.SaveSeedAsync();

        var result = await scope.CreateChangePasswordHandler().Handle(
            new ChangePasswordCommand(new ChangePasswordRequest(account.Id, "P@ssw0rd!", "N3wP@ssword!")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task LogoutCommandHandler_RevokesRefreshToken()
    {
        using var scope = _fixture.CreateScope();
        var tenant = scope.SeedTenant("Workspace", "handler-logout");
        var account = scope.SeedAccount("handler.logout@finflow.test", "P@ssw0rd!");
        account.MarkEmailVerified(scope.Clock.UtcNow);
        var membership = scope.SeedMembership(account.Id, tenant.Id, RoleType.TenantAdmin);
        scope.SeedRefreshToken("handler-logout-token", account.Id, membership.Id);
        await scope.SaveSeedAsync();

        var result = await scope.CreateLogoutHandler().Handle(
            new LogoutCommand(new LogoutRequest("handler-logout-token")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
