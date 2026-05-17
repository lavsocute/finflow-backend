using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Common;

/// <summary>
/// ISO 4217 currency code wrapper. 3 uppercase letters.
/// Use this when persisting or comparing currencies. The underlying type is
/// <see cref="string"/> so it can be stored as VARCHAR(3) without translation.
/// </summary>
public readonly record struct Currency
{
    public const int Length = 3;

    /// <summary>Vietnamese đồng — default tenant base currency.</summary>
    public static readonly Currency Vnd = new("VND");
    public static readonly Currency Usd = new("USD");
    public static readonly Currency Eur = new("EUR");
    public static readonly Currency Gbp = new("GBP");
    public static readonly Currency Jpy = new("JPY");
    public static readonly Currency Sgd = new("SGD");
    public static readonly Currency Cny = new("CNY");

    public string Code { get; }

    private Currency(string code) => Code = code;

    public static Result<Currency> Create(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<Currency>(CurrencyErrors.Required);

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != Length)
            return Result.Failure<Currency>(CurrencyErrors.InvalidLength);

        for (var i = 0; i < normalized.Length; i++)
        {
            if (normalized[i] is < 'A' or > 'Z')
                return Result.Failure<Currency>(CurrencyErrors.InvalidFormat);
        }

        return Result.Success(new Currency(normalized));
    }

    /// <summary>
    /// Throwing variant — only for trusted internal callers (DB-loaded values, constants).
    /// </summary>
    public static Currency FromTrusted(string code) => new(code);

    public override string ToString() => Code;

    public static implicit operator string(Currency currency) => currency.Code;
}

public static class CurrencyErrors
{
    public static readonly Error Required = new("Currency.Required", "Currency code is required");
    public static readonly Error InvalidLength = new("Currency.InvalidLength", "Currency code must be exactly 3 characters");
    public static readonly Error InvalidFormat = new("Currency.InvalidFormat", "Currency code must contain only uppercase letters A-Z");
    public static readonly Error MismatchBase = new("Currency.MismatchBase", "Currency does not match tenant base currency and rate is required");
    public static readonly Error InvalidRate = new("Currency.InvalidRate", "Exchange rate must be greater than zero");
}
