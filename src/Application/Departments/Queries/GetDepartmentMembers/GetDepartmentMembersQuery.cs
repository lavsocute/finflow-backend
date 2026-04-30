using FinFlow.Application.Common;
using FinFlow.Application.Departments.DTOs;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Departments.Queries.GetDepartmentMembers;

public record GetDepartmentMembersQuery(Guid TenantId, Guid DepartmentId)
    : IQuery<Result<IReadOnlyList<MemberDto>>>;