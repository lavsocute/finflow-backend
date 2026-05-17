using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.ExchangeRates.Commands.SetManualExchangeRate;

public sealed record SetManualExchangeRateCommand(
    string FromCurrency,
    string ToCurrency,
    DateOnly RateDate,
    decimal Rate) : IRequest<Result<ExchangeRateLookupResult>>;
