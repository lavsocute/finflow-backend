using FinFlow.Application.Auth.Commands.ChangePassword;
using FinFlow.Application.Auth.DTOs.Requests;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.Request.AccountId).NotEmpty();
        RuleFor(x => x.Request.CurrentPassword).NotEmpty();
        RuleFor(x => x.Request.NewPassword).NotEmpty();
    }
}
