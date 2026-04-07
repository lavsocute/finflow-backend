using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class TenantMembershipErrors
{
    public static readonly Error NotFound = new("TenantMembership.NotFound", "The tenant membership with the specified ID was not found");
    public static readonly Error AccountRequired = new("TenantMembership.AccountRequired", "Account is required");
    public static readonly Error TenantRequired = new("TenantMembership.TenantRequired", "Tenant is required");
    public static readonly Error SameRole = new("TenantMembership.SameRole", "The membership already has this role");
    public static readonly Error AlreadyDeactivated = new("TenantMembership.AlreadyDeactivated", "The membership is already deactivated");
    public static readonly Error AlreadyActive = new("TenantMembership.AlreadyActive", "The membership is already active");
}
