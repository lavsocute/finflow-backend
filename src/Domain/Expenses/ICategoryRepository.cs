namespace FinFlow.Domain.Expenses;

public record CategorySummary(
    Guid Id,
    Guid IdTenant,
    string Name,
    string? Description,
    string Icon,
    string Color,
    ExpenseCategoryType CategoryType,
    bool IsSystem,
    bool IsActive,
    int DisplayOrder);

public interface ICategoryRepository
{
    Task<CategorySummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategorySummary>> GetByTenantIdAsync(Guid idTenant, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<Category?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid idTenant, string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> HasExpensesAsync(Guid categoryId, CancellationToken cancellationToken = default);
    void Add(Category category);
    void Update(Category category);
}