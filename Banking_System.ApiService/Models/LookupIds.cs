namespace Banking_System.ApiService.Models;

// Keys of the rows in the lookup tables (Roles, Statuses, OtpPurposes). The
// database script seeds exactly these GUIDs, so the two must stay in step.

public static class RoleIds
{
    public static readonly Guid Customer = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Employee = new("10000000-0000-0000-0000-000000000002");
    public static readonly Guid Admin = new("10000000-0000-0000-0000-000000000003");
}

public static class StatusIds
{
    public static readonly Guid Active = new("20000000-0000-0000-0000-000000000001");
    public static readonly Guid Inactive = new("20000000-0000-0000-0000-000000000002");
}

public static class OtpPurposeIds
{
    public static readonly Guid PasswordReset = new("30000000-0000-0000-0000-000000000001");
    public static readonly Guid UsernameRecovery = new("30000000-0000-0000-0000-000000000002");
}
