using FinFlow.Application.Membership.Commands.InviteMember;
using FinFlow.Application.Membership.DTOs.Requests;
using FluentValidation;

namespace FinFlow.Application.Membership.Validators;

public sealed class InviteMemberCommandValidator : AbstractValidator<InviteMemberCommand>
{
    public InviteMemberCommandValidator()
    {
        RuleFor(x => x.Request.InviterAccountId).NotEmpty();
        RuleFor(x => x.Request.InviterMembershipId).NotEmpty();
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
    }
}
