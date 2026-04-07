using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Accounts;

public record AccountLoginInfo(Guid Id, string Email, string PasswordHash, string Role, Guid IdTenant, Guid IdDepartment, bool IsActive);
public record AccountSummary(Guid Id, string Email, RoleType Role, Guid IdTenant, Guid IdDepartment, bool IsActive);

public interface IAccountRepository
{
    // Read Methods (Trả về DTO, dùng Select projection - không materialize Entity)
    Task<AccountSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AccountLoginInfo?> GetLoginInfoAsync(string email, CancellationToken cancellationToken = default);
    Task<AccountLoginInfo?> GetLoginInfoByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailIgnoringTenantAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailForTenantAsync(string email, Guid idTenant, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountSummary>> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default);

    // Write Methods (Trả về Entity tracked - dùng cho update)
    Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Account account);
    void Update(Account account);
    void Remove(Account account);
}
