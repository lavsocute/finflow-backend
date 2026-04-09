using FinFlow.Application.Auth.Commands.ChangePassword;
using FinFlow.Application.Auth.Commands.Login;
using FinFlow.Application.Auth.Commands.Logout;
using FinFlow.Application.Auth.Commands.RefreshToken;
using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Auth.Validators;

namespace FinFlow.UnitTests.Application.Auth;

public sealed class AuthCommandValidatorTests
{
    [Fact]
    public void LoginCommandValidator_ReturnsErrors_ForEmptyFields()
    {
        var validator = new LoginCommandValidator();
        var command = new LoginCommand(new LoginRequest("", "", "", null));

        var result = validator.Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Email");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Password");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.TenantCode");
    }

    [Fact]
    public void RegisterCommandValidator_ReturnsErrors_ForInvalidFields()
    {
        var validator = new RegisterCommandValidator();
        var command = new RegisterCommand(new RegisterRequest("not-an-email", "", "", "", "", null));

        var result = validator.Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Email");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Password");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Name");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.TenantCode");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.DepartmentName");
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
}
