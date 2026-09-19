using System.Text.RegularExpressions;

namespace Banking_System.ApiService.Validation;

public static partial class ContactRules
{
    public const string PhonePattern = @"^\+?[0-9\s\-()]{10,20}$";

    public const string AccountOrCifPattern = @"^[A-Za-z0-9\-]{6,20}$";

    [GeneratedRegex(PhonePattern)]
    private static partial Regex PhoneRegex();

    public static bool IsValidPhone(string? phone) =>
        !string.IsNullOrWhiteSpace(phone) && PhoneRegex().IsMatch(phone);

    // Keeps a leading '+' and digits only, e.g. "+1 (555) 010-9999" -> "+15550109999".
    public static string NormalizePhone(string phone)
    {
        var trimmed = phone.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());

        return trimmed.StartsWith('+') ? "+" + digits : digits;
    }

    // Phone numbers are written many ways (with or without the country code),
    // so two numbers match when their digits are equal or one is the other
    // plus a country-code prefix.
    public static bool PhonesMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        var da = new string(a.Where(char.IsDigit).ToArray());
        var db = new string(b.Where(char.IsDigit).ToArray());

        if (da.Length < 10 || db.Length < 10)
        {
            return false;
        }

        return da == db
            || (da.Length > db.Length ? da.EndsWith(db) : db.EndsWith(da));
    }

    public static string NormalizeNationalId(string nationalId) =>
        new string(nationalId.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static bool NamesMatch(string? a, string? b) =>
        string.Equals(
            CollapseSpaces(a),
            CollapseSpaces(b),
            StringComparison.OrdinalIgnoreCase);

    private static string CollapseSpaces(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // "priya.sharma@example.com" -> "p***a@example.com"
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');

        if (at <= 1)
        {
            return "***" + email[Math.Max(at, 0)..];
        }

        return email[0] + new string('*', Math.Min(at - 2, 5) + 1) + email[at - 1] + email[at..];
    }

    // "+919876543210" -> "******3210"
    public static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        return digits.Length <= 4
            ? "****"
            : new string('*', 6) + digits[^4..];
    }
}
