using FinFlow.Domain.Expenses;

namespace FinFlow.Api.GraphQL.Categories;

public sealed class CategoryPayload
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Icon { get; set; } = null!;
    public string Color { get; set; } = null!;
    public string CategoryType { get; set; } = null!;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    public static CategoryPayload FromSummary(CategorySummary summary) => new()
    {
        Id = summary.Id,
        Name = summary.Name,
        Description = summary.Description,
        Icon = summary.Icon,
        Color = summary.Color,
        CategoryType = summary.CategoryType.ToString(),
        IsSystem = summary.IsSystem,
        IsActive = summary.IsActive,
        DisplayOrder = summary.DisplayOrder
    };
}