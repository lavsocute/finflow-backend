using FinFlow.Application.Auth.Commands.RefreshToken;
using FinFlow.Application.Auth.DTOs.Requests;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.Request.RefreshToken).NotEmpty();
    }
}
