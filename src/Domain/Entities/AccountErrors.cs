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
    public static readonly Error InvalidCurrentPassword = new("Account.InvalidPassword", "Current password is incorrect");
    public static readonly Error SameRole = new("Account.SameRole", "The account already has this role");
    public static readonly Error SameDepartment = new("Account.SameDepartment", "The account is already in this department");
    public static readonly Error AlreadyDeactivated = new("Account.AlreadyDeactivated", "The account is already deactivated");
    public static readonly Error AlreadyActive = new("Account.AlreadyActive", "The account is already active");
    public static readonly Error InvalidRole = new("Account.InvalidRole", "The account role data is invalid");
    public static readonly Error Unauthorized = new("Account.Unauthorized", "User is not authenticated or token is invalid");
    public static readonly Error TooManyRequests = new("Account.TooManyRequests", "Too many login attempts. Please try again later.");
    public static readonly Error WorkspaceSelectionRequired = new("Account.WorkspaceSelectionRequired", "Multiple workspaces found. Workspace selection is required.");
}
