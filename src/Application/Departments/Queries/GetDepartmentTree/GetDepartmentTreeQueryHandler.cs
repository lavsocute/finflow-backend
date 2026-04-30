using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Departments;
using MediatR;

namespace FinFlow.Application.Departments.Queries.GetDepartmentTree;

public sealed class GetDepartmentTreeQueryHandler : IRequestHandler<GetDepartmentTreeQuery, Result<IReadOnlyList<DepartmentTreeNodeDto>>>
{
    private readonly IDepartmentRepository _departmentRepository;

    public GetDepartmentTreeQueryHandler(IDepartmentRepository departmentRepository)
    {
        _departmentRepository = departmentRepository;
    }

    public async Task<Result<IReadOnlyList<DepartmentTreeNodeDto>>> Handle(GetDepartmentTreeQuery request, CancellationToken cancellationToken)
    {
        var departments = await _departmentRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);

        var departmentDict = departments.ToDictionary(
            d => d.Id,
            d => new DepartmentTreeNodeDto(d.Id, d.Name, d.IsActive, new List<DepartmentTreeNodeDto>())
        );

        foreach (var department in departments)
        {
            if (department.ParentId.HasValue && departmentDict.ContainsKey(department.ParentId.Value))
            {
                departmentDict[department.ParentId.Value].Children.Add(departmentDict[department.Id]);
            }
        }

        var rootNodes = departments
            .Where(d => !d.ParentId.HasValue)
            .Select(d => departmentDict[d.Id])
            .ToList();

        return Result.Success<IReadOnlyList<DepartmentTreeNodeDto>>(rootNodes);
    }
}
