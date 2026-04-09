using FinFlow.Application.Membership.Commands.AcceptInvite;
using FluentValidation;

namespace FinFlow.Application.Membership.Validators;

public sealed class AcceptInviteCommandValidator : AbstractValidator<AcceptInviteCommand>
{
    public AcceptInviteCommandValidator()
    {
        RuleFor(x => x.Request.InviteToken).NotEmpty();
        RuleFor(x => x.Request.Password).NotEmpty();
    }
}
