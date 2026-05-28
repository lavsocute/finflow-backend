using FinFlow.Application.Employees.Queries.GetMyReimbursementProfile;
using FinFlow.Application.Employees.Queries.GetReimbursementProfileForPayout;
using FinFlow.Domain.Employees;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Employees;

[ExtendObjectType(typeof(global::Query))]
public sealed class ReimbursementProfileQueries
{
    /// <summary>
    /// Caller's own profile. Bank account number is masked (last 4 only).
    /// Returns null when the staff hasn't set up a profile yet.
    /// </summary>
    [Authorize]
    public async Task<ReimbursementProfilePayload?> MyReimbursementProfileAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId");

        var result = await mediator.Send(
            new GetMyReimbursementProfileQuery(tenantId, membershipId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ReimbursementProfilePayload.FromResponse(result.Value);
    }

    /// <summary>
    /// Accountant-only payout view returning the decrypted bank account number for
    /// a specific employee membership. Every successful read is recorded in the
    /// audit log under action <c>EMPLOYEE_BANK_INFO_ACCESSED</c>.
    /// </summary>
    [Authorize]
    public async Task<ReimbursementProfilePayoutPayload> ReimbursementProfileForPayoutAsync(
        Guid membershipId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureAccountantRole(context);
        var tenantId = GetRequiredGuidClaim(context, "IdTenant");
        var accountantAccountId = GetRequiredGuidClaim(context, "sub");

        var result = await mediator.Send(
            new GetReimbursementProfileForPayoutQuery(tenantId, accountantAccountId, membershipId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ReimbursementProfilePayoutPayload.FromResponse(result.Value);
    }

    /// <summary>
    /// Static catalog of supported Vietnamese banks. Frontend uses this to render
    /// the bank dropdown when a staff edits their profile.
    /// </summary>
    public IReadOnlyList<BankCodePayload> BankCodes() =>
        VietnamBanks.All
            .Select(b => new BankCodePayload { Code = b.Code, Name = b.Name, FullName = b.FullName })
            .ToList();

    private static Guid GetRequiredGuidClaim(IResolverContext context, string claimType)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var raw = user?.FindFirst(claimType)?.Value
            ?? (claimType == "sub" ? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value : null);
        if (Guid.TryParse(raw, out var value))
            return value;

        throw new GraphQLException(new HotChocolate.Error(
            "User is not authenticated or token is invalid", "Account.Unauthorized"));
    }

    private static void EnsureAccountantRole(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawRole = user?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            ?? user?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

        if (Enum.TryParse<RoleType>(rawRole, out var role)
            && role is RoleType.Accountant or RoleType.TenantAdmin)
            return;

        throw new GraphQLException(new HotChocolate.Error(
            "Only Accountant or Admin can access employee bank info for payout.",
            "Profile.Forbidden"));
    }

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}
