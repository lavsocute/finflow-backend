using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Departments.Commands.DeactivateDepartment;

public record DeactivateDepartmentCommand(
    Guid DepartmentId,
    Guid TenantId)
    : ICommand<Result>;