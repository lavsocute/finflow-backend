using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class Account : Entity, ISoftDeletable
{
    private Account(Guid id, string email, string passwordHash)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        IsActive = true;
        IsEmailVerified = false;
        CreatedAt = DateTime.UtcNow;
    }

    private Account(Guid id, string email, string passwordHash, DateTime createdAtUtc)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        IsActive = true;
        IsEmailVerified = false;
        CreatedAt = createdAtUtc;
    }

    private Account() { }

    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public DateTime? EmailVerifiedAt { get; private set; }
    public string? FullName { get; private set; }

    public static Result<Account> Create(string email, string passwordHash)
    {
        return Create(email, passwordHash, DateTime.UtcNow);
    }

    public static Result<Account> Create(string email, string passwordHash, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<Account>(AccountErrors.EmailRequired);
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return Result.Failure<Account>(AccountErrors.InvalidEmailFormat);
        if (string.IsNullOrWhiteSpace(passwordHash))
            return Result.Failure<Account>(AccountErrors.PasswordRequired);
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<Account>(AccountErrors.InvalidCreatedAt);

        var account = new Account(Guid.NewGuid(), email.ToLowerInvariant(), passwordHash, createdAtUtc);
        account.RaiseDomainEvent(new AccountCreatedDomainEvent(account.Id, account.Email, createdAtUtc));
        return account;
    }

    public Result ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            return Result.Failure(AccountErrors.PasswordRequired);

        PasswordHash = newPasswordHash;
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

    public Result MarkEmailVerified(DateTime verifiedAtUtc)
    {
        if (verifiedAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure(AccountErrors.InvalidEmailVerifiedAt);

        if (verifiedAtUtc < CreatedAt)
            return Result.Failure(AccountErrors.EmailVerifiedBeforeCreatedAt);

        if (IsEmailVerified)
            return Result.Failure(AccountErrors.EmailAlreadyVerified);

        IsEmailVerified = true;
        EmailVerifiedAt = verifiedAtUtc;
        RaiseDomainEvent(new AccountEmailVerifiedDomainEvent(Id, verifiedAtUtc));
        return Result.Success();
    }
}
