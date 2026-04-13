using FinFlow.Application.Auth.Commands.VerifyPasswordResetToken;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class VerifyPasswordResetTokenCommandValidator : AbstractValidator<VerifyPasswordResetTokenCommand>
{
    public VerifyPasswordResetTokenCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
    }
}
