using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSettings;
using FinFlow.Domain.Abstractions;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.TenantSettings;

[ExtendObjectType(typeof(global::Query))]
public sealed class TenantSettingsQueries
{
    [Authorize]
    public async Task<TenantSettingsPayload> GetTenantSettingsAsync(
        [Service] ITenantSettingsRepository repository,
        [Service] IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        return await LoadTenantSettingsAsync(repository, unitOfWork, context, cancellationToken);
    }

    [Authorize]
    [GraphQLName("getTenantSettings")]
    public Task<TenantSettingsPayload> GetTenantSettingsLegacyAsync(
        [Service] ITenantSettingsRepository repository,
        [Service] IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        return LoadTenantSettingsAsync(repository, unitOfWork, context, cancellationToken);
    }

    [Authorize]
    public async Task<BrandingPayload> GetTenantBrandingAsync(
        [Service] ITenantSettingsRepository repository,
        [Service] IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);

        var settings = await EnsureTenantSettingsAsync(repository, unitOfWork, tenantId, cancellationToken);

        return new BrandingPayload(
            settings.LogoUrl, settings.FaviconUrl, settings.PrimaryColor,
            settings.CompanyDisplayName, settings.Locale, settings.Timezone);
    }

    private static Guid GetTenantId(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var raw = user?.FindFirst("IdTenant")?.Value;
        if (Guid.TryParse(raw, out var id)) return id;
        throw new GraphQLException(new HotChocolate.Error("Unauthorized", "Account.Unauthorized"));
    }

    private static RoleType GetRole(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawRole = user?.FindFirst(ClaimTypes.Role)?.Value ?? user?.FindFirst("role")?.Value;
        return Enum.TryParse<RoleType>(rawRole, out var role) ? role : RoleType.Staff;
    }

    private static async Task<TenantSettingsPayload> LoadTenantSettingsAsync(
        ITenantSettingsRepository repository,
        IUnitOfWork unitOfWork,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(context);
        var role = GetRole(context);

        if (role == RoleType.Staff)
            throw new GraphQLException(new HotChocolate.Error("Only Manager and above can view tenant settings.", "TenantSettings.Forbidden"));

        var settings = await EnsureTenantSettingsAsync(repository, unitOfWork, tenantId, cancellationToken);

        return TenantSettingsMutations.ToPayload(settings);
    }

    internal static async Task<Domain.Entities.TenantSettings> EnsureTenantSettingsAsync(
        ITenantSettingsRepository repository,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetByTenantIdForUpdateAsync(tenantId, cancellationToken);
        if (existing is not null)
            return existing;

        var created = Domain.Entities.TenantSettings.CreateDefault(tenantId);
        repository.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created;
    }
}
