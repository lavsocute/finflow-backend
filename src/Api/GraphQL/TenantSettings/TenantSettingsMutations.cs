using FinFlow.Api.GraphQL.Auth;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSettings;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.TenantSettings;

[ExtendObjectType(typeof(AuthMutations))]
public sealed class TenantSettingsMutations
{
    [Authorize]
    public async Task<TenantSettingsPayload> UpdateBrandingAsync(
        UpdateBrandingInput input,
        [Service] ITenantSettingsRepository repository,
        [Service] IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, _) = EnsureAuthorizedAdmin(context);

        var settings = await TenantSettingsQueries.EnsureTenantSettingsAsync(repository, unitOfWork, tenantId, cancellationToken);

        var result = settings.UpdateBranding(
            input.LogoUrl, input.FaviconUrl, input.PrimaryColor,
            input.CompanyDisplayName, input.Locale, input.Timezone);
        if (result.IsFailure) throw ToGraphQlException(result.Error);

        repository.Update(settings);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToPayload(settings);
    }

    [Authorize]
    public async Task<TenantSettingsPayload> UpdateApprovalPolicyAsync(
        UpdateApprovalPolicyInput input,
        [Service] ITenantSettingsRepository repository,
        [Service] IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, _) = EnsureAuthorizedAdmin(context);

        var settings = await TenantSettingsQueries.EnsureTenantSettingsAsync(repository, unitOfWork, tenantId, cancellationToken);

        var result = settings.UpdateApprovalPolicy(
            input.AutoApproveThreshold, input.EscalationThreshold,
            input.EscalationApproverRole, input.RequireDifferentApprover,
            input.MaxApprovalAgeHours, input.IsEscalationEnabled);
        if (result.IsFailure) throw ToGraphQlException(result.Error);

        repository.Update(settings);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToPayload(settings);
    }

    [Authorize]
    public async Task<TenantSettingsPayload> UpdateBudgetPolicyAsync(
        UpdateBudgetPolicyInput input,
        [Service] ITenantSettingsRepository repository,
        [Service] IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, _) = EnsureAuthorizedAdmin(context);

        var settings = await TenantSettingsQueries.EnsureTenantSettingsAsync(repository, unitOfWork, tenantId, cancellationToken);

        var result = settings.UpdateBudgetPolicy(
            input.DefaultEnforcementMode, input.DefaultCarryOverPercent,
            input.WarningThreshold1, input.WarningThreshold2);
        if (result.IsFailure) throw ToGraphQlException(result.Error);

        repository.Update(settings);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToPayload(settings);
    }

    [Authorize]
    public async Task<TenantSettingsPayload> UpdateReimbursementPolicyAsync(
        UpdateReimbursementPolicyInput input,
        [Service] ITenantSettingsRepository repository,
        [Service] IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, _) = EnsureAuthorizedAdmin(context);

        var settings = await TenantSettingsQueries.EnsureTenantSettingsAsync(repository, unitOfWork, tenantId, cancellationToken);

        var result = settings.UpdateReimbursementPolicy(
            input.MaxClaimAmount, input.ReceiptRequiredAbove);
        if (result.IsFailure) throw ToGraphQlException(result.Error);

        repository.Update(settings);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToPayload(settings);
    }

    [Authorize]
    public async Task<TenantSettingsPayload> UpdateNotificationPreferencesAsync(
        UpdateNotificationPreferencesInput input,
        [Service] ITenantSettingsRepository repository,
        [Service] IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var (tenantId, _) = EnsureAuthorizedAdmin(context);

        var settings = await TenantSettingsQueries.EnsureTenantSettingsAsync(repository, unitOfWork, tenantId, cancellationToken);

        var result = settings.UpdateNotificationPreferences(
            input.EmailDigestEnabled, input.EmailDigestFrequency);
        if (result.IsFailure) throw ToGraphQlException(result.Error);

        repository.Update(settings);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToPayload(settings);
    }

    // ─── Helpers ───

    private static (Guid TenantId, Guid MembershipId) EnsureAuthorizedAdmin(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var tenantId = GetRequiredGuidClaim(user, "IdTenant");
        var membershipId = GetRequiredGuidClaim(user, "MembershipId");

        var rawRole = user?.FindFirst(ClaimTypes.Role)?.Value ?? user?.FindFirst("role")?.Value;
        if (!Enum.TryParse<RoleType>(rawRole, out var role) || role != RoleType.TenantAdmin)
            throw ToGraphQlException(new DomainError("TenantSettings.Forbidden", "Only TenantAdmin can manage tenant settings."));

        return (tenantId, membershipId);
    }

    private static Guid GetRequiredGuidClaim(ClaimsPrincipal? user, string claimType)
    {
        var rawValue = user?.FindFirst(claimType)?.Value;
        if (Guid.TryParse(rawValue, out var value)) return value;
        throw new GraphQLException(new HotChocolate.Error("Unauthorized", "Account.Unauthorized"));
    }

    internal static TenantSettingsPayload ToPayload(Domain.Entities.TenantSettings s) => new(
        s.Id,
        new BrandingPayload(s.LogoUrl, s.FaviconUrl, s.PrimaryColor, s.CompanyDisplayName, s.Locale, s.Timezone),
        new ApprovalPolicyPayload(s.AutoApproveThreshold, s.EscalationThreshold, s.EscalationApproverRole.ToString(), s.RequireDifferentApprover, s.MaxApprovalAgeHours, s.IsEscalationEnabled),
        new BudgetPolicyPayload(s.DefaultEnforcementMode.ToString(), s.DefaultCarryOverPercent, s.WarningThreshold1, s.WarningThreshold2),
        new ReimbursementPolicyPayload(s.MaxClaimAmount, s.ReceiptRequiredAbove),
        new NotificationPreferencesPayload(s.EmailDigestEnabled, s.EmailDigestFrequency),
        s.UpdatedAt);

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}
