using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Departments.Commands.ChangeParentDepartment;

public sealed class ChangeParentDepartmentCommandHandler
    : ICommandHandler<ChangeParentDepartmentCommand, Result<DepartmentSummaryDto>>
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeParentDepartmentCommandHandler(
        IDepartmentRepository departmentRepository,
        IUnitOfWork unitOfWork)
    {
        _departmentRepository = departmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DepartmentSummaryDto>> Handle(
        ChangeParentDepartmentCommand request,
        CancellationToken cancellationToken = default)
    {
        var department = await _departmentRepository.GetEntityByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null)
            return Result.Failure<DepartmentSummaryDto>(DepartmentErrors.NotFound);

        if (department.IdTenant != request.TenantId)
            return Result.Failure<DepartmentSummaryDto>(DepartmentErrors.NotFound);

        if (request.NewParentId.HasValue)
        {
            var parent = await _departmentRepository.GetEntityByIdAsync(request.NewParentId.Value, cancellationToken);
            if (parent is null)
                return Result.Failure<DepartmentSummaryDto>(DepartmentErrors.NotFound);
            if (parent.IdTenant != request.TenantId)
                return Result.Failure<DepartmentSummaryDto>(DepartmentErrors.NotFound);
            if (!parent.IsActive)
                return Result.Failure<DepartmentSummaryDto>(DepartmentErrors.Inactive);
        }

        var changeResult = department.ChangeParent(request.NewParentId);
        if (changeResult.IsFailure)
            return Result.Failure<DepartmentSummaryDto>(changeResult.Error);

        _departmentRepository.Update(department);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new DepartmentSummaryDto(
            department.Id,
            department.Name,
            department.ParentId,
            department.IsActive));
    }
}
