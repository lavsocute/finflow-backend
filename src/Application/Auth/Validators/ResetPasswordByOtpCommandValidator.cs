using FinFlow.Application.Auth.Commands.ResetPasswordByOtp;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class ResetPasswordByOtpCommandValidator : AbstractValidator<ResetPasswordByOtpCommand>
{
    public ResetPasswordByOtpCommandValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Otp).NotEmpty().Length(6);
        RuleFor(x => x.Request.NewPassword).NotEmpty();
    }
}
