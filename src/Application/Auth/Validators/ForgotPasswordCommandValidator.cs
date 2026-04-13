using FinFlow.Application.Auth.Commands.ForgotPassword;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
    }
}
