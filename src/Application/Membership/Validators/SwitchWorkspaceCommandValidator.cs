using FinFlow.Application.Membership.Commands.SwitchWorkspace;
using FinFlow.Application.Membership.DTOs.Requests;
using FluentValidation;

namespace FinFlow.Application.Membership.Validators;

public sealed class SwitchWorkspaceCommandValidator : AbstractValidator<SwitchWorkspaceCommand>
{
    public SwitchWorkspaceCommandValidator()
    {
        RuleFor(x => x.Request.AccountId).NotEmpty();
        RuleFor(x => x.Request.MembershipId).NotEmpty();
        RuleFor(x => x.Request.CurrentRefreshToken).NotEmpty();
    }
}
