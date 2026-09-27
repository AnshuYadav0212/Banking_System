namespace Banking_System.ApiService.Validation;

/// <summary>
/// Decides who can self-register as staff: anyone with an email address on the
/// bank's internal staff domain. This is not identity-verified like customer
/// registration is - keeping that domain restricted to addresses the bank
/// actually controls is what makes it safe.
/// </summary>
public static class StaffEmailRules
{
    public const string EmployeeDomain = "admin.com";

    public static bool IsEmployeeEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Trim().EndsWith("@" + EmployeeDomain, StringComparison.OrdinalIgnoreCase);
}
