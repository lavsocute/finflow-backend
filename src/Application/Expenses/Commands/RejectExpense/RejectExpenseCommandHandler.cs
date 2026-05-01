using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Expenses.Commands.RejectExpense;

internal sealed class RejectExpenseCommandHandler : IRequestHandler<RejectExpenseCommand, Result<Unit>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public RejectExpenseCommandHandler(
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

    public async Task<Result<Unit>> Handle(RejectExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.GetEntityByIdAsync(request.ExpenseId, cancellationToken);
        if (expense is null)
            return Result.Failure<Unit>(ExpenseErrors.NotFound);

        if (expense.Status != ExpenseStatus.Confirmed)
            return Result.Failure<Unit>(ExpenseErrors.AlreadyProcessed);

        var rejectResult = expense.Reject(request.Reason);
        if (rejectResult.IsFailure)
            return Result.Failure<Unit>(rejectResult.Error);

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