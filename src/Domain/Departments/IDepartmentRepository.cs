namespace FinFlow.Domain.Departments;

public record DepartmentSummary(Guid Id, string Name, Guid IdTenant, Guid? ParentId, bool IsActive);

public interface IDepartmentRepository
{
    // Read Methods (DTO)
    Task<DepartmentSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DepartmentSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default);

    // Write Methods (Entity)
    void Add(FinFlow.Domain.Entities.Department department);
    void Update(FinFlow.Domain.Entities.Department department);
    void Remove(FinFlow.Domain.Entities.Department department);
}
