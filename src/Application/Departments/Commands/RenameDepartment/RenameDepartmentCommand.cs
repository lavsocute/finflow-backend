using FinFlow.Application.Common;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Departments.Commands.RenameDepartment;

public record RenameDepartmentCommand(
    Guid DepartmentId,
    Guid TenantId,
    string Name)
    : ICommand<Result<DepartmentSummaryDto>>;