using System.Security.Claims;
using FinFlow.Api.GraphQL.Auth;
using FinFlow.Application.ExchangeRates.Commands.SetManualExchangeRate;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.ExchangeRates;

[ExtendObjectType(typeof(AuthMutations))]
public sealed class ExchangeRateMutations
{
    /// <summary>
    /// Insert a manual exchange rate snapshot. Restricted to TenantAdmin so a single
    /// authoritative source can override or backfill rates when the upstream provider
    /// is unavailable. Manual entries are preferred over provider data on read.
    /// </summary>
    [Authorize]
    public async Task<ExchangeRateResponse> SetManualExchangeRateAsync(
        SetManualExchangeRateInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureTenantAdmin(context);

        var result = await mediator.Send(
            new SetManualExchangeRateCommand(
                input.FromCurrency,
                input.ToCurrency,
                input.RateDate,
                input.Rate),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return ExchangeRateResponse.FromLookup(
            input.FromCurrency.ToUpperInvariant(),
            input.ToCurrency.ToUpperInvariant(),
            input.RateDate,
            result.Value);
    }

    private static void EnsureTenantAdmin(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var rawRole = user?.FindFirst(ClaimTypes.Role)?.Value
            ?? user?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

        if (Enum.TryParse<RoleType>(rawRole, out var role) && role == RoleType.TenantAdmin)
            return;

        throw new GraphQLException(new HotChocolate.Error(
            "Only TenantAdmin can set manual exchange rates.",
            "ExchangeRate.Forbidden"));
    }
}
