using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Expenses;

public sealed class Category : Entity, IMultiTenant, ISoftDeletable
{
    private Category(
        Guid id,
        Guid idTenant,
        string name,
        string? description,
        string icon,
        string color,
        ExpenseCategoryType categoryType,
        bool isSystem,
        bool isActive,
        int displayOrder,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        IdTenant = idTenant;
        Name = name;
        Description = description;
        Icon = icon;
        Color = color;
        CategoryType = categoryType;
        IsSystem = isSystem;
        IsActive = isActive;
        DisplayOrder = displayOrder;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

private Category() { }

    public Guid IdTenant { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Icon { get; private set; } = null!;
    public string Color { get; private set; } = null!;
    public ExpenseCategoryType CategoryType { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    internal static Category CreateForSeeding(
        Guid id,
        Guid idTenant,
        string name,
        string? description,
        string icon,
        string color,
        ExpenseCategoryType categoryType,
        bool isSystem,
        bool isActive,
        int displayOrder,
        DateTime createdAt,
        DateTime updatedAt)
    {
        return new Category(
            id,
            idTenant,
            name,
            description,
            icon,
            color,
            categoryType,
            isSystem,
            isActive,
            displayOrder,
            createdAt,
            updatedAt);
    }

    public static Result<Category> CreateSystem(
        Guid idTenant,
        ExpenseCategoryType categoryType,
        string name,
        string? description,
        string icon,
        string color,
        int displayOrder)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<Category>(CategoryErrors.TenantRequired);
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Category>(CategoryErrors.NameRequired);

        var now = DateTime.UtcNow;
        return Result.Success(new Category(
            Guid.NewGuid(),
            idTenant,
            name.Trim(),
            description?.Trim(),
            icon.Trim(),
            color.Trim(),
            categoryType,
            isSystem: true,
            isActive: true,
            displayOrder,
            now,
            now));
    }

    public static Result<Category> CreateUserDefined(
        Guid idTenant,
        string name,
        string? description,
        string icon,
        string color,
        int displayOrder)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<Category>(CategoryErrors.TenantRequired);
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Category>(CategoryErrors.NameRequired);

        var now = DateTime.UtcNow;
        return Result.Success(new Category(
            Guid.NewGuid(),
            idTenant,
            name.Trim(),
            description?.Trim(),
            icon.Trim(),
            color.Trim(),
            ExpenseCategoryType.Other,
            isSystem: false,
            isActive: true,
            displayOrder,
            now,
            now));
    }

    public Result Update(string name, string? description, string icon, string color)
    {
        if (IsSystem)
            return Result.Failure(CategoryErrors.CannotModifySystemCategory);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Category>(CategoryErrors.NameRequired);

        Name = name.Trim();
        Description = description?.Trim();
        Icon = icon.Trim();
        Color = color.Trim();
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }

    public Result Deactivate()
    {
        if (IsSystem)
            return Result.Failure(CategoryErrors.CannotModifySystemCategory);

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }

    public Result Activate()
    {
        if (IsSystem)
            return Result.Failure(CategoryErrors.CannotModifySystemCategory);

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }
}

public enum ExpenseCategoryType
{
    OfficeSupplies,
    Travel,
    Equipment,
    Software,
    Marketing,
    Training,
    Utilities,
    Food,
    Transportation,
    Communication,
    Professional,
    Other
}