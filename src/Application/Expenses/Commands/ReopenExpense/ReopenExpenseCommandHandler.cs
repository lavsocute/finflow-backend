using FinFlow.Application.Budgets.Services;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Expenses.Commands.ReopenExpense;

internal sealed class ReopenExpenseCommandHandler : IRequestHandler<ReopenExpenseCommand, Result<Unit>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IBudgetReservationService _budgetReservation;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public ReopenExpenseCommandHandler(
        IExpenseRepository expenseRepository,
        IBudgetReservationService budgetReservation,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _expenseRepository = expenseRepository;
        _budgetReservation = budgetReservation;
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

        var reapply = await _budgetReservation.ReapplySpentAsync(
            new BudgetMovement(
                TenantId: expense.IdTenant,
                DepartmentId: expense.IdDepartment,
                Month: expense.Month,
                Year: expense.Year,
                AmountInBaseCurrency: expense.AmountInBaseCurrency,
                SourceEntityId: expense.Id,
                SourceEntityType: "Expense",
                Reason: $"Reopened: {request.Reason}"),
            BudgetExceededTrigger.ConfirmPayment,
            cancellationToken);
        if (reapply.IsFailure)
            return Result.Failure<Unit>(reapply.Error);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Unit.Value);
    }
}
