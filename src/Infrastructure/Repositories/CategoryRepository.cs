using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class CategoryRepository : ICategoryRepository
{
    private readonly ApplicationDbContext _dbContext;

    public CategoryRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<CategorySummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Category>()
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategorySummary(
                c.Id,
                c.IdTenant,
                c.Name,
                c.Description,
                c.Icon,
                c.Color,
                c.CategoryType,
                c.IsSystem,
                c.IsActive,
                c.DisplayOrder))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CategorySummary>> GetByTenantIdAsync(Guid idTenant, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Category>()
            .AsNoTracking()
            .Where(c => c.IdTenant == idTenant);

        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        return await query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategorySummary(
                c.Id,
                c.IdTenant,
                c.Name,
                c.Description,
                c.Icon,
                c.Color,
                c.CategoryType,
                c.IsSystem,
                c.IsActive,
                c.DisplayOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<Category?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Category>()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<bool> ExistsAsync(Guid idTenant, string name, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Category>()
            .AnyAsync(c =>
                c.IdTenant == idTenant &&
                c.Name.ToLower() == name.Trim().ToLower() &&
                (!excludeId.HasValue || c.Id != excludeId.Value),
                cancellationToken);

    public async Task<bool> HasExpensesAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Expense>()
            .AnyAsync(e => e.IdCategory == categoryId, cancellationToken);

    public void Add(Category category) => _dbContext.Set<Category>().Add(category);
    public void Update(Category category) => _dbContext.Set<Category>().Update(category);
}