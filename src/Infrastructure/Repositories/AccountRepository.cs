using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class AccountRepository : IAccountRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AccountRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    // Read Method: Returns DTO via Select projection
    public async Task<AccountSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>()
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AccountSummary(a.Id, a.Email, a.Role, a.IdTenant, a.IdDepartment, a.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    // Read Method: Returns DTO via Select projection
    public async Task<AccountLoginInfo?> GetLoginInfoAsync(string email, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>()
            .AsNoTracking()
            .Where(a => a.Email == email.ToLowerInvariant())
            .Select(a => new AccountLoginInfo(a.Id, a.Email, a.PasswordHash, a.Role.ToString(), a.IdTenant, a.IdDepartment, a.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    // Read Method: Returns DTO via Select projection 
    public async Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.Id == id)
            .Select(a => new AccountLoginInfo(a.Id, a.Email, a.PasswordHash, a.Role.ToString(), a.IdTenant, a.IdDepartment, a.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    // Read Method: Exists check ignoring tenant scope 
    public async Task<bool> ExistsByEmailIgnoringTenantAsync(string email, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>().IgnoreQueryFilters().AnyAsync(a => a.Email == email.ToLowerInvariant(), cancellationToken);

    // Read Method: Exists check scoped to tenant (includes deactivated accounts to prevent reuse)
    public async Task<bool> ExistsByEmailForTenantAsync(string email, Guid idTenant, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>()
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Email == email.ToLowerInvariant() && a.IdTenant == idTenant, cancellationToken);

    // Read Method: Returns List of DTOs via Select projection
    public async Task<IReadOnlyList<AccountSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>()
            .AsNoTracking()
            .Where(a => a.IdTenant == idTenant)
            .Select(a => new AccountSummary(a.Id, a.Email, a.Role, a.IdTenant, a.IdDepartment, a.IsActive))
            .ToListAsync(cancellationToken);

    // Write Method: Returns tracked Entity for updates
    public async Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    // Write Methods
    public void Add(Account account) => _dbContext.Set<Account>().Add(account);
    public void Update(Account account) => _dbContext.Set<Account>().Update(account);
    public void Remove(Account account) => _dbContext.Set<Account>().Remove(account);
}
