using FinFlow.Application.Common;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Departments.Commands.ChangeParentDepartment;

public record ChangeParentDepartmentCommand(
    Guid DepartmentId,
    Guid TenantId,
    Guid? NewParentId)
    : ICommand<Result<DepartmentSummaryDto>>;
