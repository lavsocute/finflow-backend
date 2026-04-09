using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class DepartmentErrors
{
    public static readonly Error NotFound = new("Department.NotFound", "The department with the specified ID was not found");
    public static readonly Error NameRequired = new("Department.NameRequired", "Department name is required");
    public static readonly Error NameTooLong = new("Department.NameTooLong", "Department name cannot exceed 100 characters");
    public static readonly Error Inactive = new("Department.Inactive", "The department is inactive");
    public static readonly Error AlreadyDeactivated = new("Department.AlreadyDeactivated", "The department is already deactivated");
    public static readonly Error AlreadyActive = new("Department.AlreadyActive", "The department is already active");
    public static readonly Error CannotBeOwnParent = new("Department.CannotBeOwnParent", "A department cannot be its own parent");
}
