using FinFlow.Application.Auth.Commands.VerifyEmailByOtp;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class VerifyEmailByOtpCommandValidator : AbstractValidator<VerifyEmailByOtpCommand>
{
    public VerifyEmailByOtpCommandValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Otp).NotEmpty().Length(6);
    }
}
