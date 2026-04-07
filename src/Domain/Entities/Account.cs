using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class Account : Entity, IMultiTenant
{
    private Account(Guid id, string email, string passwordHash, RoleType role, Guid idTenant, Guid idDepartment)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        IdTenant = idTenant;
        IdDepartment = idDepartment;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    private Account() { }

    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public RoleType Role { get; private set; }
    public Guid IdTenant { get; private set; }
    public Guid IdDepartment { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<Account> Create(string email, string passwordHash, RoleType role, Guid idTenant, Guid idDepartment)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<Account>(AccountErrors.EmailRequired);
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return Result.Failure<Account>(AccountErrors.InvalidEmailFormat);
        if (string.IsNullOrWhiteSpace(passwordHash))
            return Result.Failure<Account>(AccountErrors.PasswordRequired);

        var account = new Account(Guid.NewGuid(), email.ToLowerInvariant(), passwordHash, role, idTenant, idDepartment);
        account.RaiseDomainEvent(new AccountCreatedDomainEvent(account.Id, account.Email, account.IdTenant));
        return account;
    }

    public Result ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            return Result.Failure(AccountErrors.PasswordRequired);

        PasswordHash = newPasswordHash;
        return Result.Success();
    }

    public Result ChangeRole(RoleType newRole)
    {
        if (Role == newRole) return Result.Failure(AccountErrors.SameRole);
        Role = newRole;
        RaiseDomainEvent(new AccountRoleChangedDomainEvent(Id, newRole));
        return Result.Success();
    }

    public Result ChangeDepartment(Guid idDepartment)
    {
        if (IdDepartment == idDepartment) return Result.Failure(AccountErrors.SameDepartment);
        IdDepartment = idDepartment;
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive) return Result.Failure(AccountErrors.AlreadyDeactivated);
        IsActive = false;
        RaiseDomainEvent(new AccountDeactivatedDomainEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive) return Result.Failure(AccountErrors.AlreadyActive);
        IsActive = true;
        RaiseDomainEvent(new AccountActivatedDomainEvent(Id));
        return Result.Success();
    }
}