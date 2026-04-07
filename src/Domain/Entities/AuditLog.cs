using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public sealed class AuditLog : Entity
{
    private AuditLog(Guid id, string action, string entityType, string? entityId, string? oldValue, string? newValue, string? ipAddress, string? userAgent, Guid? idTenant, Guid? idAccount)
    {
        Id = id;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OldValue = oldValue;
        NewValue = newValue;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        IdTenant = idTenant;
        IdAccount = idAccount;
        Timestamp = DateTime.UtcNow;
    }

    private AuditLog() { }

    public string Action { get; private set; } = null!; // CREATE, UPDATE, DELETE, LOGIN, LOGOUT, CHANGE_PASSWORD
    public string EntityType { get; private set; } = null!; // Tenant, Account, Expense, etc.
    public string? EntityId { get; private set; }
    public string? OldValue { get; private set; } 
    public string? NewValue { get; private set; } 
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public Guid? IdTenant { get; private set; }
    public Guid? IdAccount { get; private set; }
    public DateTime Timestamp { get; private set; }

    public static AuditLog Create(string action, string entityType, string? entityId = null, string? oldValue = null, string? newValue = null, string? ipAddress = null, string? userAgent = null, Guid? idTenant = null, Guid? idAccount = null)
    {
        return new AuditLog(
            Guid.NewGuid(),
            action,
            entityType,
            entityId,
            oldValue,
            newValue,
            ipAddress,
            userAgent,
            idTenant,
            idAccount);
    }
}
