using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.UnitTests;

public sealed class DomainEntityTests
{
    [Fact]
    public void Invitation_MarkAccepted_Succeeds_WhenPending()
    {
        var invitation = Invitation.Create(
            "user@finflow.test",
            Guid.NewGuid(),
            Guid.NewGuid(),
            RoleType.Staff,
            "raw-invite-token",
            DateTime.UtcNow.AddDays(1)).Value;

        var result = invitation.MarkAccepted();

        Assert.True(result.IsSuccess);
        Assert.NotNull(invitation.AcceptedAt);
    }

    [Fact]
    public void Invitation_MarkAccepted_Fails_WhenExpired()
    {
        var invitation = Invitation.Create(
            "user@finflow.test",
            Guid.NewGuid(),
            Guid.NewGuid(),
            RoleType.Staff,
            "raw-invite-token",
            DateTime.UtcNow.AddSeconds(1)).Value;

        Thread.Sleep(1200);
        var result = invitation.MarkAccepted();

        Assert.True(result.IsFailure);
        Assert.Equal(InvitationErrors.Expired.Code, result.Error.Code);
    }

    [Fact]
    public void RefreshToken_ReplaceWith_RevokesOldToken_AndReturnsNewRawToken()
    {
        var token = RefreshToken.Create("old-raw-token", Guid.NewGuid(), Guid.NewGuid(), 7).Value;

        var result = token.ReplaceWith("new-raw-token", 7);

        Assert.True(result.IsSuccess);
        Assert.True(token.IsRevoked);
        Assert.Equal("Replaced", token.ReasonRevoked);
        Assert.Equal(RefreshToken.HashToken("new-raw-token"), token.ReplacedByToken);
        Assert.Equal("new-raw-token", result.Value.RawToken);
        Assert.True(result.Value.NewToken.IsActive);
        Assert.Equal(token.AccountId, result.Value.NewToken.AccountId);
        Assert.Equal(token.MembershipId, result.Value.NewToken.MembershipId);
    }

    [Fact]
    public void TenantMembership_Create_Fails_WhenTenantMissing()
    {
        var result = TenantMembership.Create(Guid.NewGuid(), Guid.Empty, RoleType.Staff);

        Assert.True(result.IsFailure);
        Assert.Equal(TenantMembershipErrors.TenantRequired.Code, result.Error.Code);
    }

    [Fact]
    public void TenantMembership_ChangeRole_Deactivate_Activate_FollowExpectedRules()
    {
        var membership = TenantMembership.Create(Guid.NewGuid(), Guid.NewGuid(), RoleType.Staff).Value;

        var roleResult = membership.ChangeRole(RoleType.Manager);
        var deactivateResult = membership.Deactivate();
        var secondDeactivate = membership.Deactivate();
        var activateResult = membership.Activate();

        Assert.True(roleResult.IsSuccess);
        Assert.Equal(RoleType.Manager, membership.Role);
        Assert.True(deactivateResult.IsSuccess);
        Assert.True(secondDeactivate.IsFailure);
        Assert.Equal(TenantMembershipErrors.AlreadyDeactivated.Code, secondDeactivate.Error.Code);
        Assert.True(activateResult.IsSuccess);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public void TenantMembership_Create_Fails_WhenOwnerIsNotTenantAdmin()
    {
        var result = TenantMembership.Create(Guid.NewGuid(), Guid.NewGuid(), RoleType.Manager, isOwner: true);

        Assert.True(result.IsFailure);
        Assert.Equal(TenantMembershipErrors.OwnerMustBeTenantAdmin.Code, result.Error.Code);
    }
}
