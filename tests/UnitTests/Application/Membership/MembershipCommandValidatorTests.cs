using FinFlow.Application.Membership.Commands.AcceptInvite;
using FinFlow.Application.Membership.Commands.InviteMember;
using FinFlow.Application.Membership.Commands.SwitchWorkspace;
using FinFlow.Application.Membership.DTOs.Requests;
using FinFlow.Application.Membership.Validators;
using FinFlow.Domain.Enums;

namespace FinFlow.UnitTests.Application.Membership;

public sealed class MembershipCommandValidatorTests
{
    [Fact]
    public void SwitchWorkspaceCommandValidator_ReturnsErrors_ForMissingFields()
    {
        var validator = new SwitchWorkspaceCommandValidator();
        var command = new SwitchWorkspaceCommand(new SwitchWorkspaceRequest(Guid.Empty, Guid.Empty, ""));

        var result = validator.Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == "Request.AccountId");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.MembershipId");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.CurrentRefreshToken");
    }

    [Fact]
    public void InviteMemberCommandValidator_ReturnsErrors_ForInvalidEmailAndMissingIds()
    {
        var validator = new InviteMemberCommandValidator();
        var command = new InviteMemberCommand(new InviteMemberRequest(Guid.Empty, Guid.Empty, "invalid", RoleType.Staff));

        var result = validator.Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == "Request.InviterAccountId");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.InviterMembershipId");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Email");
    }

    [Fact]
    public void AcceptInviteCommandValidator_ReturnsErrors_ForMissingFields()
    {
        var validator = new AcceptInviteCommandValidator();
        var command = new AcceptInviteCommand(new AcceptInviteRequest("", "", null));

        var result = validator.Validate(command);

        Assert.Contains(result.Errors, x => x.PropertyName == "Request.InviteToken");
        Assert.Contains(result.Errors, x => x.PropertyName == "Request.Password");
    }
}
