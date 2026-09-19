using System.Text.RegularExpressions;

namespace Banking_System.ApiService.Validation;

public static partial class UsernameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 30;

    public const string Pattern = "^[A-Za-z0-9_]{3,30}$";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "root", "system", "support", "help", "employee",
        "customer", "securebank", "api", "login", "logout", "register", "null"
    };

    [GeneratedRegex(Pattern)]
    private static partial Regex FormatRegex();

    public static bool IsValidFormat(string? username) =>
        !string.IsNullOrEmpty(username) && FormatRegex().IsMatch(username);

    public static bool IsReserved(string username) => Reserved.Contains(username);

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();
}
