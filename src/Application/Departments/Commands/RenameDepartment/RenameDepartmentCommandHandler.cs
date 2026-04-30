using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Departments.Commands.RenameDepartment;

public sealed class RenameDepartmentCommandHandler : ICommandHandler<RenameDepartmentCommand, Result<DepartmentSummaryDto>>
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RenameDepartmentCommandHandler(IDepartmentRepository departmentRepository, IUnitOfWork unitOfWork)
    {
        _departmentRepository = departmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DepartmentSummaryDto>> Handle(RenameDepartmentCommand request, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetEntityByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null)
            return Result.Failure<DepartmentSummaryDto>(DepartmentErrors.NotFound);

        if (department.IdTenant != request.TenantId)
            return Result.Failure<DepartmentSummaryDto>(DepartmentErrors.NotFound);

        var renameResult = department.Rename(request.Name);
        if (renameResult.IsFailure)
            return Result.Failure<DepartmentSummaryDto>(renameResult.Error);

        _departmentRepository.Update(department);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new DepartmentSummaryDto(
            department.Id,
            department.Name,
            department.ParentId,
            department.IsActive));
    }
}