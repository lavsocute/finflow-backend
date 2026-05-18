using FinFlow.Application.Budgets.Services;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Expenses.Commands.RejectExpense;

internal sealed class RejectExpenseCommandHandler : IRequestHandler<RejectExpenseCommand, Result<Unit>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IBudgetReservationService _budgetReservation;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public RejectExpenseCommandHandler(
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

    public async Task<Result<Unit>> Handle(RejectExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.GetEntityByIdAsync(request.ExpenseId, cancellationToken);
        if (expense is null)
            return Result.Failure<Unit>(ExpenseErrors.NotFound);

        if (expense.Status != ExpenseStatus.Confirmed)
            return Result.Failure<Unit>(ExpenseErrors.AlreadyProcessed);

        var rejectResult = expense.Reject(request.Reason, _currentTenant.MembershipId);
        if (rejectResult.IsFailure)
            return Result.Failure<Unit>(rejectResult.Error);

        _expenseRepository.Update(expense);

        var release = await _budgetReservation.ReverseSpentAsync(
            new BudgetMovement(
                TenantId: expense.IdTenant,
                DepartmentId: expense.IdDepartment,
                Month: expense.Month,
                Year: expense.Year,
                AmountInBaseCurrency: expense.AmountInBaseCurrency,
                SourceEntityId: expense.Id,
                SourceEntityType: "Expense",
                Reason: $"Rejected: {request.Reason}"),
            cancellationToken);
        if (release.IsFailure)
            return Result.Failure<Unit>(release.Error);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Unit.Value);
    }
}
