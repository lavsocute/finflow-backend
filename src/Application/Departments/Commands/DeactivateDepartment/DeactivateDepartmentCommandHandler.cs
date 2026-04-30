using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Departments.Commands.DeactivateDepartment;

public sealed class DeactivateDepartmentCommandHandler : ICommandHandler<DeactivateDepartmentCommand, Result>
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeactivateDepartmentCommandHandler(IDepartmentRepository departmentRepository, IUnitOfWork unitOfWork)
    {
        _departmentRepository = departmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeactivateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetEntityByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null)
            return Result.Failure(DepartmentErrors.NotFound);

        if (department.IdTenant != request.TenantId)
            return Result.Failure(DepartmentErrors.NotFound);

        var deactivateResult = department.Deactivate();
        if (deactivateResult.IsFailure)
            return deactivateResult;

        _departmentRepository.Update(department);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}