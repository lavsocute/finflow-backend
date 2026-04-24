using FinFlow.Application.Auth.Commands.VerifyEmailByToken;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class VerifyEmailByTokenCommandValidator : AbstractValidator<VerifyEmailByTokenCommand>
{
    public VerifyEmailByTokenCommandValidator()
    {
        RuleFor(x => x.Request.Token).NotEmpty().MaximumLength(500);
    }
}
