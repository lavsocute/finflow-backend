using FinFlow.Domain.Common;
using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public TenancyModel TenancyModel { get; set; } = TenancyModel.Shared;
    public string? ConnectionString { get; set; }
    public string Currency { get; set; } = "VND";

    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}