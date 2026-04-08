using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class InvitationErrors
{
    public static readonly Error NotFound = new("Invitation.NotFound", "The invitation with the specified token was not found");
    public static readonly Error EmailRequired = new("Invitation.EmailRequired", "Invitee email is required");
    public static readonly Error InvalidEmail = new("Invitation.InvalidEmail", "Invitee email format is invalid");
    public static readonly Error TenantRequired = new("Invitation.TenantRequired", "Tenant is required");
    public static readonly Error InviterMembershipRequired = new("Invitation.InviterMembershipRequired", "Inviter membership is required");
    public static readonly Error InvalidRole = new("Invitation.InvalidRole", "The selected role cannot be invited");
    public static readonly Error TokenRequired = new("Invitation.TokenRequired", "Invitation token is required");
    public static readonly Error ExpirationRequired = new("Invitation.ExpirationRequired", "Invitation expiration must be in the future");
    public static readonly Error PendingInvitationExists = new("Invitation.PendingExists", "A pending invitation already exists for this email in the current workspace");
    public static readonly Error AlreadyMember = new("Invitation.AlreadyMember", "This account is already a member of the current workspace");
    public static readonly Error Forbidden = new("Invitation.Forbidden", "You do not have permission to invite members to this workspace");
    public static readonly Error AlreadyAccepted = new("Invitation.AlreadyAccepted", "This invitation has already been accepted");
    public static readonly Error AlreadyRevoked = new("Invitation.AlreadyRevoked", "This invitation has already been revoked");
    public static readonly Error Expired = new("Invitation.Expired", "This invitation has expired");
}
