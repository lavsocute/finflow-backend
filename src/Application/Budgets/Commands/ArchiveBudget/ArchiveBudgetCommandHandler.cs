using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Budgets.Commands.ArchiveBudget;

public sealed class ArchiveBudgetCommandHandler : ICommandHandler<ArchiveBudgetCommand, Result>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ArchiveBudgetCommandHandler(IBudgetRepository budgetRepository, IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ArchiveBudgetCommand request, CancellationToken cancellationToken)
    {
        var entity = await _budgetRepository.GetEntityByIdAsync(request.BudgetId, request.TenantId, cancellationToken);
        if (entity is null)
            return Result.Failure(BudgetErrors.NotFound);

        // Don't allow archive when there are outstanding commitments — they
        // would be silently lost (and the lifecycle release/spent moves would
        // fail their lookup with no compensating event).
        if (entity.CommittedAmount > 0)
            return Result.Failure(BudgetErrors.CannotArchiveActive);

        var result = entity.Archive();
        if (result.IsFailure)
            return result;

        _budgetRepository.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
