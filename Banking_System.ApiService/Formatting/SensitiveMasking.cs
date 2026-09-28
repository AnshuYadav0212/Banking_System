namespace Banking_System.ApiService.Formatting;

/// <summary>
/// Masks personal identifiers for staff-facing lists: numbers keep only their last
/// 4 digits, email addresses keep only the first letter and the domain.
/// </summary>
public static class SensitiveMasking
{
    /// <summary>"1000005" -> "••••0005". Works for CIF and phone numbers (formatting characters are ignored).</summary>
    public static string MaskNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = new string(value.Where(char.IsLetterOrDigit).ToArray());

        return digits.Length <= 4 ? "••••" : "••••" + digits[^4..];
    }

    /// <summary>"advik@gmail.com" -> "a••••@gmail.com".</summary>
    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.IndexOf('@');

        return at <= 0
            ? "••••"
            : email[0] + "••••" + email[at..];
    }
}
