using FinFlow.Application.Common;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Departments.Queries.GetDepartmentTree;

public record GetDepartmentTreeQuery(Guid TenantId)
    : IQuery<Result<IReadOnlyList<DepartmentTreeNodeDto>>>;
