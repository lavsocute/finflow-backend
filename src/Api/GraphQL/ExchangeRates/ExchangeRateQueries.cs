using FinFlow.Application.Common.ExchangeRates;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace FinFlow.Api.GraphQL.ExchangeRates;

[ExtendObjectType(typeof(global::Query))]
public sealed class ExchangeRateQueries
{
    /// <summary>
    /// Lookup the exchange rate for converting <paramref name="fromCurrency"/> into
    /// <paramref name="toCurrency"/> on the given date. Returns the persisted snapshot
    /// when one exists; otherwise fetches from the configured upstream provider and
    /// caches it. Used by the frontend to preview the conversion before submitting.
    /// </summary>
    [Authorize]
    public async Task<ExchangeRateResponse> ExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly date,
        [Service] IExchangeRateService exchangeRateService,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        _ = context; // tenant context preserved by middleware; rates are global
        var result = await exchangeRateService.GetRateAsync(fromCurrency, toCurrency, date, cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return ExchangeRateResponse.FromLookup(fromCurrency.ToUpperInvariant(), toCurrency.ToUpperInvariant(), date, result.Value);
    }
}
