using FinFlow.Application.Auth.Commands.Logout;
using FinFlow.Application.Auth.DTOs.Requests;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.Request.RefreshToken).NotEmpty();
    }
}
