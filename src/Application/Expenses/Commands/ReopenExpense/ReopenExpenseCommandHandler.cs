using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Expenses.Commands.ReopenExpense;

internal sealed class ReopenExpenseCommandHandler : IRequestHandler<ReopenExpenseCommand, Result<Unit>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public ReopenExpenseCommandHandler(
        IExpenseRepository expenseRepository,
        IBudgetRepository budgetRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _expenseRepository = expenseRepository;
        _budgetRepository = budgetRepository;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit>> Handle(ReopenExpenseCommand request, CancellationToken cancellationToken)
    {
        if (!_currentTenant.MembershipId.HasValue)
            return Result.Failure<Unit>(new Error("Expense.MembershipContext", "Membership context is not available."));

        var expense = await _expenseRepository.GetEntityByIdAsync(request.ExpenseId, cancellationToken);
        if (expense is null)
            return Result.Failure<Unit>(ExpenseErrors.NotFound);

        var reopenResult = expense.Reopen(request.Reason, _currentTenant.MembershipId.Value);
        if (reopenResult.IsFailure)
            return Result.Failure<Unit>(reopenResult.Error);

        _expenseRepository.Update(expense);

        var budget = await _budgetRepository.GetByDepartmentAndPeriodAsync(
            expense.IdDepartment, expense.Month, expense.Year, cancellationToken);

        if (budget != null)
        {
            var spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
                expense.IdDepartment, expense.Month, expense.Year, cancellationToken);

            var budgetEntity = await _budgetRepository.GetEntityByIdAsync(budget.Id, cancellationToken);
            if (budgetEntity != null)
            {
                budgetEntity.RecalculateSpent(spentAmount);
                _budgetRepository.Update(budgetEntity);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Unit.Value);
    }
}
