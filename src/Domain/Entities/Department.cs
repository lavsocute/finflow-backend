using FinFlow.Domain.Common;

namespace FinFlow.Domain.Entities;

public class Department : BaseEntity
{
    public Guid IdTenant { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;
    public Department? Parent { get; set; }
    public ICollection<Department> Children { get; set; } = new List<Department>();
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}