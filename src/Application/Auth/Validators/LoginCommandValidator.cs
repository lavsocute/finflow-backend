using FinFlow.Application.Auth.Commands.Login;
using FinFlow.Application.Auth.DTOs.Requests;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Password).NotEmpty();
        RuleFor(x => x.Request.TenantCode).NotEmpty();
    }
}
