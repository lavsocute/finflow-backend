namespace FinFlow.Domain.Interfaces;

/// <summary>
/// Marker interface for entities that should be soft-deleted (set IsActive = false)
/// instead of physically removed. Entities NOT implementing this interface will be
/// hard-deleted by the database.
/// </summary>
public interface ISoftDeletable
{
    bool IsActive { get; }
}
