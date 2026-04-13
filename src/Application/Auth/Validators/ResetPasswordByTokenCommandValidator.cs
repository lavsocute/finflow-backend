using FinFlow.Application.Auth.Commands.ResetPasswordByToken;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class ResetPasswordByTokenCommandValidator : AbstractValidator<ResetPasswordByTokenCommand>
{
    public ResetPasswordByTokenCommandValidator()
    {
        RuleFor(x => x.Request.Token).NotEmpty();
        RuleFor(x => x.Request.NewPassword).NotEmpty();
    }
}
