namespace FinFlow.Application.Departments.DTOs;

public record DepartmentSummaryDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    bool IsActive);

public record DepartmentTreeNodeDto(
    Guid Id,
    string Name,
    bool IsActive,
    List<DepartmentTreeNodeDto> Children);

public record MemberDto(
    Guid Id,
    string Email,
    string Role,
    bool IsActive);
