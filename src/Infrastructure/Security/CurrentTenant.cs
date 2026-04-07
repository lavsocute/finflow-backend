using FinFlow.Domain.Interfaces;

namespace FinFlow.Infrastructure.Security;

public class CurrentTenant : ICurrentTenant
{
    public Guid? Id { get; set; }
    public bool IsAvailable => Id.HasValue;
    public bool IsSuperAdmin { get; set; }
}
