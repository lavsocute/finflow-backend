using FinFlow.Application.Common;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Departments.Commands.CreateDepartment;

public record CreateDepartmentCommand(
    Guid TenantId,
    string Name,
    Guid? ParentId)
    : ICommand<Result<DepartmentSummaryDto>>;
