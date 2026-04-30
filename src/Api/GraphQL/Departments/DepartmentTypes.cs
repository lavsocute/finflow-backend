using FinFlow.Application.Departments.DTOs;

namespace FinFlow.Api.GraphQL.Departments;

public sealed class DepartmentSummaryType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; }

    public static DepartmentSummaryType FromDto(DepartmentSummaryDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        ParentId = dto.ParentId,
        IsActive = dto.IsActive
    };
}

public sealed class DepartmentTreeNodeType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
    public List<DepartmentTreeNodeType> Children { get; set; } = new();

    public static DepartmentTreeNodeType FromDto(DepartmentTreeNodeDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        IsActive = dto.IsActive,
        Children = dto.Children.Select(DepartmentTreeNodeType.FromDto).ToList()
    };
}

public sealed class DepartmentMemberType
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
    public bool IsActive { get; set; }

    public static DepartmentMemberType FromDto(MemberDto dto) => new()
    {
        Id = dto.Id,
        Email = dto.Email,
        Role = dto.Role,
        IsActive = dto.IsActive
    };
}