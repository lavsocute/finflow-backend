using FinFlow.Domain.ExchangeRates;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class ExchangeRateRepository : IExchangeRateRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ExchangeRateRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public Task<ExchangeRateHistory?> GetByKeyAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        CancellationToken cancellationToken = default) =>
        _dbContext.ExchangeRateHistory
            .AsNoTracking()
            .Where(x => x.FromCurrency == fromCurrency
                     && x.ToCurrency == toCurrency
                     && x.RateDate == rateDate)
            // Prefer manual override > provider > system seed.
            .OrderByDescending(x => x.Source == ExchangeRateSource.Manual)
            .ThenByDescending(x => x.Source == ExchangeRateSource.Provider)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ExchangeRateHistory?> GetNearestAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        int windowDays,
        CancellationToken cancellationToken = default)
    {
        var minDate = rateDate.AddDays(-windowDays);
        var maxDate = rateDate.AddDays(windowDays);

        // Prefer same/earlier date; if none, the nearest later date is acceptable.
        return _dbContext.ExchangeRateHistory
            .AsNoTracking()
            .Where(x => x.FromCurrency == fromCurrency
                     && x.ToCurrency == toCurrency
                     && x.RateDate >= minDate
                     && x.RateDate <= maxDate)
            .OrderBy(x => x.RateDate > rateDate ? 1 : 0)        // prefer earlier or same
            .ThenByDescending(x => x.RateDate <= rateDate ? x.RateDate : DateOnly.MinValue)
            .ThenBy(x => x.RateDate > rateDate ? x.RateDate : DateOnly.MaxValue)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExchangeRateHistory>> GetRangeAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        await _dbContext.ExchangeRateHistory
            .AsNoTracking()
            .Where(x => x.FromCurrency == fromCurrency
                     && x.ToCurrency == toCurrency
                     && x.RateDate >= fromDate
                     && x.RateDate <= toDate)
            .OrderBy(x => x.RateDate)
            .ToListAsync(cancellationToken);

    public void Add(ExchangeRateHistory entry) => _dbContext.ExchangeRateHistory.Add(entry);
}
