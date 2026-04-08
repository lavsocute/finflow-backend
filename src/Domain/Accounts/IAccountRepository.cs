using FinFlow.Domain.Entities;

namespace FinFlow.Domain.Accounts;

public record AccountLoginInfo(Guid Id, string Email, string PasswordHash, bool IsActive);
public record AccountSummary(Guid Id, string Email, Guid IdDepartment, bool IsActive);

public interface IAccountRepository
{
    // Read Methods (Trả về DTO, dùng Select projection - không materialize Entity)
    Task<AccountSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AccountLoginInfo?> GetLoginInfoByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailIgnoringTenantAsync(string email, CancellationToken cancellationToken = default);

    // Write Methods (Trả về Entity tracked - dùng cho update)
    Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Account account);
    void Update(Account account);
    void Remove(Account account);
}
