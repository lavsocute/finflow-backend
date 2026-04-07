namespace FinFlow.Domain.Interfaces;

public interface ICurrentTenant
{
    Guid? Id { get; set; }
    bool IsAvailable { get; }
    bool IsSuperAdmin { get; set; } // Phân biệt Super Admin với request chưa có context
}
