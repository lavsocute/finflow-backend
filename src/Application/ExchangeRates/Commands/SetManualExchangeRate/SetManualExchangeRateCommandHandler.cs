using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.ExchangeRates;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.ExchangeRates.Commands.SetManualExchangeRate;

internal sealed class SetManualExchangeRateCommandHandler
    : IRequestHandler<SetManualExchangeRateCommand, Result<ExchangeRateLookupResult>>
{
    private readonly IExchangeRateRepository _repository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public SetManualExchangeRateCommandHandler(
        IExchangeRateRepository repository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ExchangeRateLookupResult>> Handle(SetManualExchangeRateCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<ExchangeRateLookupResult>(
                new Error("ExchangeRate.MembershipContext", "Membership context is required to set manual exchange rate."));

        var entryResult = ExchangeRateHistory.Create(
            request.FromCurrency,
            request.ToCurrency,
            request.RateDate,
            request.Rate,
            ExchangeRateSource.Manual,
            _currentTenant.MembershipId);

        if (entryResult.IsFailure)
            return Result.Failure<ExchangeRateLookupResult>(entryResult.Error);

        _repository.Add(entryResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ExchangeRateLookupResult(
            entryResult.Value.Rate,
            entryResult.Value.RateDate,
            entryResult.Value.Source));
    }
}
