using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class Department : Entity, IMultiTenant
{
    private Department(Guid id, string name, Guid idTenant, Guid? parentId)
    {
        Id = id;
        Name = name;
        IdTenant = idTenant;
        ParentId = parentId;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    private Department() { }

    public string Name { get; private set; } = null!;
    public Guid IdTenant { get; private set; }
    public Guid? ParentId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<Department> Create(string name, Guid idTenant, Guid? parentId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Department>(DepartmentErrors.NameRequired);
        if (name.Length > 100)
            return Result.Failure<Department>(DepartmentErrors.NameTooLong);

        var department = new Department(Guid.NewGuid(), name, idTenant, parentId);
        department.RaiseDomainEvent(new DepartmentCreatedDomainEvent(department.Id, department.Name, department.IdTenant));
        return department;
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return Result.Failure(DepartmentErrors.NameRequired);
        Name = name;
        return Result.Success();
    }

    public Result ChangeParent(Guid? parentId)
    {
        if (parentId == Id) return Result.Failure(DepartmentErrors.CannotBeOwnParent);
        ParentId = parentId;
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive) return Result.Failure(DepartmentErrors.AlreadyDeactivated);
        IsActive = false;
        RaiseDomainEvent(new DepartmentDeactivatedDomainEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive) return Result.Failure(DepartmentErrors.AlreadyActive);
        IsActive = true;
        RaiseDomainEvent(new DepartmentActivatedDomainEvent(Id));
        return Result.Success();
    }
}