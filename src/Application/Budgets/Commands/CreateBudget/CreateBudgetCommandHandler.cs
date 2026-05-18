using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Tenants;

namespace FinFlow.Application.Budgets.Commands.CreateBudget;

public sealed class CreateBudgetCommandHandler : ICommandHandler<CreateBudgetCommand, Result<BudgetDetailDto>>
{
    private const string DefaultBaseCurrency = "VND";

    private readonly IBudgetRepository _budgetRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBudgetCommandHandler(
        IBudgetRepository budgetRepository,
        IDepartmentRepository departmentRepository,
        ITenantRepository tenantRepository,
        IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _departmentRepository = departmentRepository;
        _tenantRepository = tenantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BudgetDetailDto>> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null || department.IdTenant != request.TenantId)
            return Result.Failure<BudgetDetailDto>(DepartmentErrors.NotFound);

        var exists = await _budgetRepository.ExistsAsync(request.TenantId, request.DepartmentId, request.Month, request.Year, cancellationToken);
        if (exists)
            return Result.Failure<BudgetDetailDto>(BudgetErrors.DuplicateBudget);

        // Snapshot tenant base currency at create time. The Budget then carries
        // this value forever (immutable) so a future tenant currency change
        // doesn't silently re-interpret historical allocations.
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
        var baseCurrency = string.IsNullOrWhiteSpace(tenant?.Currency)
            ? DefaultBaseCurrency
            : tenant!.Currency;

        var createResult = Budget.Create(
            request.TenantId,
            request.DepartmentId,
            request.Month,
            request.Year,
            request.Amount,
            baseCurrencyCode: baseCurrency);

        if (createResult.IsFailure)
            return Result.Failure<BudgetDetailDto>(createResult.Error);

        var budget = createResult.Value;
        _budgetRepository.Add(budget);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Brand-new budget — Spent and Committed are 0, no aggregation needed.
        return Result.Success(new BudgetDetailDto(
            budget.Id,
            budget.IdDepartment,
            department.Name,
            budget.Month,
            budget.Year,
            budget.AllocatedAmount,
            budget.SpentAmount,
            budget.AvailableAmount,
            budget.IsOverSpent,
            budget.IsNearLimit));
    }
}
