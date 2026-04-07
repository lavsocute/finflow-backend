using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class AccountRepository : IAccountRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AccountRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<AccountLoginInfo?> GetLoginInfoAsync(string email, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<Account>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email.ToLowerInvariant(), cancellationToken);

        if (account == null) return null;

        return new AccountLoginInfo(account.Id, account.Email, account.PasswordHash, account.Role.ToString(), account.IdTenant, account.IdDepartment, account.IsActive);
    }

    public async Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<Account>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (account == null) return null;

        return new AccountLoginInfo(account.Id, account.Email, account.PasswordHash, account.Role.ToString(), account.IdTenant, account.IdDepartment, account.IsActive);
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>().IgnoreQueryFilters().AnyAsync(a => a.Email == email.ToLowerInvariant(), cancellationToken);

    public async Task<IReadOnlyList<Account>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Account>().AsNoTracking().Where(a => a.IdTenant == idTenant).ToListAsync(cancellationToken);

    public void Add(Account account) => _dbContext.Set<Account>().Add(account);
    public void Update(Account account) => _dbContext.Set<Account>().Update(account);
    public void Remove(Account account) => _dbContext.Set<Account>().Remove(account);
}
