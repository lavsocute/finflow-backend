using FinFlow.Api.GraphQL.Auth;
using FinFlow.Application.Bank.Commands.ExportPaymentsToBankCsv;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using System.Security.Claims;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Bank;

[ExtendObjectType(typeof(AuthMutations))]
public sealed class BankExportMutations
{
    /// <summary>
    /// Export N pending BankTransfer payments to a single bank-format CSV file.
    /// Restricted to Accountant + TenantAdmin. Each call emits one
    /// <c>PAYMENTS_EXPORTED_TO_CSV</c> audit row plus N
    /// <c>EMPLOYEE_BANK_INFO_ACCESSED</c> audit rows (one per distinct employee).
    /// </summary>
    [Authorize]
    public async Task<ExportPaymentsToBankCsvPayload> ExportPaymentsToBankCsvAsync(
        ExportPaymentsToBankCsvInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureExportRole(context);

        var tenantId = GetRequiredGuidClaim(context, "IdTenant");
        var accountId = GetRequiredGuidClaim(context, "sub");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId");

        var result = await mediator.Send(
            new ExportPaymentsToBankCsvCommand(
                TenantId: tenantId,
                AccountantAccountId: accountId,
                AccountantMembershipId: membershipId,
                PaymentIds: input.PaymentIds,
                BankFormat: input.BankFormat),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ExportPaymentsToBankCsvPayload.FromResponse(result.Value);
    }

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

    private static void EnsureExportRole(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawRole = user?.FindFirst(ClaimTypes.Role)?.Value
            ?? user?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

        if (Enum.TryParse<RoleType>(rawRole, out var role)
            && role is RoleType.Accountant or RoleType.TenantAdmin)
            return;

        throw new GraphQLException(new HotChocolate.Error(
            "Only Accountant or Admin can export payments to a bank file.",
            "BankExport.Forbidden"));
    }

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));
}
