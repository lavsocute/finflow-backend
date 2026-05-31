using FinFlow.Application.Chat.Cascade;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

public sealed class ChatIntentExemplarRepository : IChatIntentExemplarRepository
{
    private readonly ApplicationDbContext _db;

    public ChatIntentExemplarRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ChatIntentExemplar>> GetActiveAsync(
        string embeddingModel,
        Guid? tenantId,
        CancellationToken ct)
    {
        var query = _db.Set<ChatIntentExemplar>()
            .Where(e => e.IsActive && e.EmbeddingModel == embeddingModel);

        query = tenantId.HasValue
            ? query.Where(e => e.IdTenant == null || e.IdTenant == tenantId)
            : query.Where(e => e.IdTenant == null);

        return await query.AsNoTracking().ToListAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<ChatIntentExemplar> exemplars, CancellationToken ct)
    {
        await _db.Set<ChatIntentExemplar>().AddRangeAsync(exemplars, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAllAsync(Guid? tenantId, CancellationToken ct)
    {
        var query = _db.Set<ChatIntentExemplar>().AsQueryable();
        query = tenantId.HasValue
            ? query.Where(e => e.IdTenant == tenantId)
            : query.Where(e => e.IdTenant == null);
        var existing = await query.ToListAsync(ct);
        _db.Set<ChatIntentExemplar>().RemoveRange(existing);
        await _db.SaveChangesAsync(ct);
    }
}
