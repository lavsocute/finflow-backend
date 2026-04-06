using FinFlow.Domain.Common;
using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Entities;

public class Account : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public RoleType Role { get; set; }
    public Guid IdTenant { get; set; }
    public Guid IdDepartment { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Department Department { get; set; } = null!;
}