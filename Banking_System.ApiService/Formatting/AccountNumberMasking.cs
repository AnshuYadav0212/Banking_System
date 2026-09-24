namespace Banking_System.ApiService.Formatting;

/// <summary>
/// Masks account numbers before they leave the API. The full number is only ever
/// used internally (lookups, transfers); anything returned to a client shows just
/// the last 4 digits.
/// </summary>
public static class AccountNumberMasking
{
    public static string Mask(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return string.Empty;
        }

        var trimmed = accountNumber.Trim();
        return trimmed.Length <= 4
            ? trimmed
            : "••••" + trimmed[^4..];
    }
}
