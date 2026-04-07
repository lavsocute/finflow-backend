using FinFlow.Domain.Entities;

namespace FinFlow.Domain.Accounts;

public record AccountLoginInfo(Guid Id, string Email, string PasswordHash, string Role, Guid IdTenant, Guid IdDepartment, bool IsActive);

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AccountLoginInfo?> GetLoginInfoAsync(string email, CancellationToken cancellationToken = default);
    Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default);
    void Add(Account account);
    void Update(Account account);
    void Remove(Account account);
}
