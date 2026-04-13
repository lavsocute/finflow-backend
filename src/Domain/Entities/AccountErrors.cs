using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class AccountErrors
{
    public static readonly Error NotFound = new("Account.NotFound", "The account with the specified ID was not found");
    public static readonly Error EmailRequired = new("Account.EmailRequired", "Email is required");
    public static readonly Error InvalidEmailFormat = new("Account.InvalidEmail", "Email format is invalid");
    public static readonly Error EmailAlreadyExists = new("Account.EmailExists", "An account with this email already exists");
    public static readonly Error PasswordRequired = new("Account.PasswordRequired", "Password is required");
    public static readonly Error PasswordTooShort = new("Account.PasswordTooShort", "Password must be at least 8 characters");
    public static readonly Error PasswordTooWeak = new("Account.PasswordTooWeak", "Password must contain uppercase, lowercase, number, and special character");
    public static readonly Error InvalidCurrentPassword = new("Account.InvalidPassword", "Current password is incorrect");
    public static readonly Error SameDepartment = new("Account.SameDepartment", "The account is already in this department");
    public static readonly Error AlreadyDeactivated = new("Account.AlreadyDeactivated", "The account is already deactivated");
    public static readonly Error AlreadyActive = new("Account.AlreadyActive", "The account is already active");
    public static readonly Error EmailNotVerified = new("Account.EmailNotVerified", "The account email has not been verified");
    public static readonly Error EmailAlreadyVerified = new("Account.EmailAlreadyVerified", "The account email is already verified");
    public static readonly Error InvalidCreatedAt = new("Account.InvalidCreatedAt", "Account created timestamp must be UTC");
    public static readonly Error InvalidEmailVerifiedAt = new("Account.InvalidEmailVerifiedAt", "Email verified timestamp must be UTC");
    public static readonly Error EmailVerifiedBeforeCreatedAt = new("Account.EmailVerifiedBeforeCreatedAt", "Email verified timestamp cannot be earlier than account creation");
    public static readonly Error Unauthorized = new("Account.Unauthorized", "User is not authenticated or token is invalid");
    public static readonly Error TooManyRequests = new("Account.TooManyRequests", "Too many login attempts. Please try again later.");
    public static readonly Error WorkspaceSelectionRequired = new("Account.WorkspaceSelectionRequired", "Multiple workspaces found. Workspace selection is required.");
}
