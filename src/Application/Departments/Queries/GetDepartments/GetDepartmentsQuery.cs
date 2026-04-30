using FinFlow.Application.Common;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Departments.Queries.GetDepartments;

public record GetDepartmentsQuery(Guid TenantId)
    : IQuery<Result<IReadOnlyList<DepartmentSummaryDto>>>;