using System.Text.RegularExpressions;

namespace FinFlow.Application.Auth.Support;

internal static class PasswordRules
{
    private static readonly Regex StrongPasswordRegex = new(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$",
        RegexOptions.Compiled);

    public static bool IsStrong(string password) =>
        !string.IsNullOrWhiteSpace(password) && StrongPasswordRegex.IsMatch(password);
}
