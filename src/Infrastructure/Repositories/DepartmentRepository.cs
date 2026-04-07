using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class DepartmentRepository : IDepartmentRepository
{
    private readonly ApplicationDbContext _dbContext;

    public DepartmentRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<DepartmentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DepartmentSummary(d.Id, d.Name, d.IdTenant, d.ParentId, d.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DepartmentSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => d.IdTenant == idTenant)
            .Select(d => new DepartmentSummary(d.Id, d.Name, d.IdTenant, d.ParentId, d.IsActive))
            .ToListAsync(cancellationToken);

    public void Add(Department department) => _dbContext.Set<Department>().Add(department);
    public void Update(Department department) => _dbContext.Set<Department>().Update(department);
    public void Remove(Department department) => _dbContext.Set<Department>().Remove(department);
}
