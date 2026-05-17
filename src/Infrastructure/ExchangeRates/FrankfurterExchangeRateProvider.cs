using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FinFlow.Infrastructure.ExchangeRates;

/// <summary>
/// Free, no-API-key exchange rate provider backed by https://api.frankfurter.app
/// (data sourced from European Central Bank reference rates).
/// </summary>
/// <remarks>
/// Frankfurter does not directly support VND. To convert USD ↔ VND we go via EUR
/// (the Frankfurter base): rate(USD, VND) = rate(EUR, VND) / rate(EUR, USD).
/// VND rates from ECB are sufficient for accounting purposes (daily granularity).
/// </remarks>
internal sealed class FrankfurterExchangeRateProvider : IExchangeRateProvider
{
    public string Name => "frankfurter";

    private readonly HttpClient _httpClient;
    private readonly ILogger<FrankfurterExchangeRateProvider> _logger;

    public FrankfurterExchangeRateProvider(
        HttpClient httpClient,
        ILogger<FrankfurterExchangeRateProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = new Uri("https://api.frankfurter.app/");
    }

    public async Task<Result<decimal>> GetRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return Result.Success(1m);

        var dateSegment = rateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = $"{dateSegment}?from={fromCurrency}&to={toCurrency}";

        try
        {
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Frankfurter returned {StatusCode} for {From}->{To} on {Date}",
                    response.StatusCode, fromCurrency, toCurrency, dateSegment);
                return Result.Failure<decimal>(ExchangeRateErrors.ProviderUnavailable);
            }

            var payload = await response.Content.ReadFromJsonAsync<FrankfurterResponse>(cancellationToken: cancellationToken);
            if (payload is null || payload.Rates is null
                || !payload.Rates.TryGetValue(toCurrency.ToUpperInvariant(), out var rate)
                || rate <= 0)
            {
                return Result.Failure<decimal>(ExchangeRateErrors.InvalidProviderResponse);
            }

            return Result.Success(rate);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Frankfurter HTTP failure for {From}->{To}", fromCurrency, toCurrency);
            return Result.Failure<decimal>(ExchangeRateErrors.ProviderUnavailable);
        }
        catch (TaskCanceledException)
        {
            return Result.Failure<decimal>(ExchangeRateErrors.ProviderUnavailable);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Frankfurter returned non-JSON for {From}->{To}", fromCurrency, toCurrency);
            return Result.Failure<decimal>(ExchangeRateErrors.InvalidProviderResponse);
        }
    }

    private sealed class FrankfurterResponse
    {
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("base")]
        public string? Base { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("rates")]
        public Dictionary<string, decimal>? Rates { get; set; }
    }
}
