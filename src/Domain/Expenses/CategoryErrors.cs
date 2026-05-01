using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Expenses;

public static class CategoryErrors
{
    public static readonly Error NotFound = new("Category.NotFound", "Category not found");
    public static readonly Error NotFoundByName = new("Category.NotFoundByName", "Category not found by name");
    public static readonly Error DuplicatedName = new("Category.DuplicatedName", "Category name already exists");
    public static readonly Error CannotDeleteSystemCategory = new("Category.CannotDeleteSystem", "System categories cannot be deleted");
    public static readonly Error CannotModifySystemCategory = new("Category.CannotModifySystem", "System categories cannot be modified");
    public static readonly Error CannotDeleteCategoryWithExpenses = new("Category.HasExpenses", "Cannot delete category with expenses");
    public static readonly Error TenantRequired = new("Category.TenantRequired", "Tenant ID is required");
    public static readonly Error NameRequired = new("Category.NameRequired", "Category name is required");
    public static readonly Error InvalidIcon = new("Category.InvalidIcon", "Icon is required");
    public static readonly Error InvalidColor = new("Category.InvalidColor", "Color must be valid hex");
    public static readonly Error CategoryTypeRequired = new("Category.CategoryTypeRequired", "Category type is required");
}