namespace Banking_System.Web.Models.Banking;

/// <summary>Masks account numbers for display so only the last 4 digits are ever shown on screen.</summary>
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
