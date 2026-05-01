namespace FinFlow.Domain.Expenses;

public static class CategorySeedData
{
    public static IReadOnlyList<Category> GetDefaultCategories(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        return new List<Category>
        {
            CreateSystem(tenantId, ExpenseCategoryType.OfficeSupplies, "Office Supplies", "Office supplies and stationery", "📎", "#3B82F6", 1),
            CreateSystem(tenantId, ExpenseCategoryType.Travel, "Travel", "Travel and transportation expenses", "✈️", "#8B5CF6", 2),
            CreateSystem(tenantId, ExpenseCategoryType.Equipment, "Equipment", "Hardware and equipment purchases", "💻", "#10B981", 3),
            CreateSystem(tenantId, ExpenseCategoryType.Software, "Software", "Software licenses and subscriptions", "🔧", "#F59E0B", 4),
            CreateSystem(tenantId, ExpenseCategoryType.Marketing, "Marketing", "Marketing and advertising expenses", "📢", "#EC4899", 5),
            CreateSystem(tenantId, ExpenseCategoryType.Training, "Training", "Employee training and development", "🎓", "#06B6D4", 6),
            CreateSystem(tenantId, ExpenseCategoryType.Utilities, "Utilities", "Utility bills and services", "💡", "#84CC16", 7),
            CreateSystem(tenantId, ExpenseCategoryType.Food, "Food", "Food and catering expenses", "🍽️", "#F97316", 8),
            CreateSystem(tenantId, ExpenseCategoryType.Transportation, "Transportation", "Transportation and logistics", "🚗", "#6366F1", 9),
            CreateSystem(tenantId, ExpenseCategoryType.Communication, "Communication", "Communication expenses", "📱", "#14B8A6", 10),
            CreateSystem(tenantId, ExpenseCategoryType.Professional, "Professional Services", "Professional and consulting services", "👔", "#A855F7", 11),
            CreateSystem(tenantId, ExpenseCategoryType.Other, "Other", "Miscellaneous expenses", "📦", "#64748B", 12)
        };
    }

    private static Category CreateSystem(
        Guid tenantId,
        ExpenseCategoryType type,
        string name,
        string? description,
        string icon,
        string color,
        int displayOrder)
    {
        var now = DateTime.UtcNow;
        return Category.CreateForSeeding(
            Guid.NewGuid(),
            tenantId,
            name,
            description,
            icon,
            color,
            type,
            isSystem: true,
            isActive: true,
            displayOrder,
            now,
            now);
    }
}