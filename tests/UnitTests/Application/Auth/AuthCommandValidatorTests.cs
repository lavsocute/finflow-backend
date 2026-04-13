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
using FinFlow.Application.Auth.Commands.VerifyPasswordResetToken;
using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.Validators;

namespace FinFlow.UnitTests.Application.Auth;

public sealed class AuthCommandValidatorTests
{
    [Fact]
    public void LoginCommandValidator_ReturnsErrors_ForEmptyFields()
    {
        var validator = new LoginCommandValidator();
        var command = new LoginCommand(new LoginRequest("", "", null));

        var result = validator.Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Email");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Password");
    }

    [Fact]
    public void RegisterCommandValidator_ReturnsErrors_ForInvalidFields()
    {
        var validator = new RegisterCommandValidator();
        var command = new RegisterCommand(new RegisterRequest("not-an-email", "", "", null));

        var result = validator.Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Email");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Password");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Name");
    }

    [Fact]
    public void RefreshTokenAndLogoutValidators_ReturnErrors_ForEmptyRefreshToken()
    {
        var refreshValidator = new RefreshTokenCommandValidator();
        var logoutValidator = new LogoutCommandValidator();

        var refreshResult = refreshValidator.Validate(new RefreshTokenCommand(new RefreshTokenRequest("")));
        var logoutResult = logoutValidator.Validate(new LogoutCommand(new LogoutRequest("")));

        Assert.Contains(refreshResult.Errors, x => x.PropertyName == "Request.RefreshToken");
        Assert.Contains(logoutResult.Errors, x => x.PropertyName == "Request.RefreshToken");
    }

    [Fact]
    public void ChangePasswordCommandValidator_ReturnsErrors_ForMissingFields()
    {
        var validator = new ChangePasswordCommandValidator();
        var command = new ChangePasswordCommand(new ChangePasswordRequest(Guid.Empty, "", ""));

        var result = validator.Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == "Request.AccountId");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.CurrentPassword");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.NewPassword");
    }

    [Fact]
    public void VerifyEmailAndResendValidators_ReturnErrors_ForMissingFields()
    {
        var verifyByTokenValidator = new VerifyEmailByTokenCommandValidator();
        var verifyByOtpValidator = new VerifyEmailByOtpCommandValidator();
        var resendValidator = new ResendEmailVerificationCommandValidator();

        var verifyByTokenResult = verifyByTokenValidator.Validate(new VerifyEmailByTokenCommand(new VerifyEmailByTokenRequest("")));
        var verifyByOtpResult = verifyByOtpValidator.Validate(new VerifyEmailByOtpCommand(new VerifyEmailByOtpRequest("", "")));
        var resendResult = resendValidator.Validate(new ResendEmailVerificationCommand(new ResendEmailVerificationRequest("")));

        Assert.Contains(verifyByTokenResult.Errors, x => x.PropertyName == "Request.Token");
        Assert.Contains(verifyByOtpResult.Errors, x => x.PropertyName == "Request.Email");
        Assert.Contains(verifyByOtpResult.Errors, x => x.PropertyName == "Request.Otp");
        Assert.Contains(resendResult.Errors, x => x.PropertyName == "Request.Email");
    }

    [Fact]
    public void ForgotAndResetPasswordValidators_ReturnErrors_ForMissingFields()
    {
        var forgotValidator = new ForgotPasswordCommandValidator();
        var verifyTokenValidator = new VerifyPasswordResetTokenCommandValidator();
        var tokenResetValidator = new ResetPasswordByTokenCommandValidator();
        var otpResetValidator = new ResetPasswordByOtpCommandValidator();

        var forgotResult = forgotValidator.Validate(new ForgotPasswordCommand(new ForgotPasswordRequest("")));
        var verifyResult = verifyTokenValidator.Validate(new VerifyPasswordResetTokenCommand(""));
        var tokenResetResult = tokenResetValidator.Validate(new ResetPasswordByTokenCommand(new ResetPasswordByTokenRequest("", "")));
        var otpResetResult = otpResetValidator.Validate(new ResetPasswordByOtpCommand(new ResetPasswordByOtpRequest("", "", "")));

        Assert.Contains(forgotResult.Errors, x => x.PropertyName == "Request.Email");
        Assert.Contains(verifyResult.Errors, x => x.PropertyName == "Token");
        Assert.Contains(tokenResetResult.Errors, x => x.PropertyName == "Request.Token");
        Assert.Contains(tokenResetResult.Errors, x => x.PropertyName == "Request.NewPassword");
        Assert.Contains(otpResetResult.Errors, x => x.PropertyName == "Request.Email");
        Assert.Contains(otpResetResult.Errors, x => x.PropertyName == "Request.Otp");
        Assert.Contains(otpResetResult.Errors, x => x.PropertyName == "Request.NewPassword");
    }
}
