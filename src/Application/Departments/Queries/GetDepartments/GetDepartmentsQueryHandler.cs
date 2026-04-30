using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Departments;
using MediatR;

namespace FinFlow.Application.Departments.Queries.GetDepartments;

public sealed class GetDepartmentsQueryHandler : IRequestHandler<GetDepartmentsQuery, Result<IReadOnlyList<DepartmentSummaryDto>>>
{
    private readonly IDepartmentRepository _departmentRepository;

    public GetDepartmentsQueryHandler(IDepartmentRepository departmentRepository)
    {
        _departmentRepository = departmentRepository;
    }

    public async Task<Result<IReadOnlyList<DepartmentSummaryDto>>> Handle(GetDepartmentsQuery request, CancellationToken cancellationToken)
    {
        var departments = await _departmentRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);

        var responses = departments
            .Select(d => new DepartmentSummaryDto(
                d.Id,
                d.Name,
                d.ParentId,
                d.IsActive))
            .ToList();

        return Result.Success<IReadOnlyList<DepartmentSummaryDto>>(responses);
    }
}