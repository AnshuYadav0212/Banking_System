namespace Banking_System.ApiService.Models;

// Keys of the rows in the lookup tables (Roles, Statuses, OtpPurposes). The
// database script seeds exactly these GUIDs, so the two must stay in step.

public static class RoleIds
{
    public static readonly Guid Customer = new("2AD39D82-4527-421F-BC48-0024F41392C3");
    public static readonly Guid Employee = new("8AFC0CD1-50D7-4CDE-9555-C336E07C632E");
    public static readonly Guid Admin = new("6B7A8B89-B9A0-4B50-B800-3806C6FDAB09");
}

public static class StatusIds
{
    public static readonly Guid Active = new("29394BF4-EB03-4808-B2B4-5452F352D506");
    public static readonly Guid Inactive = new("036BA01A-4FD0-4AB4-8A43-21B08A9DC67F");
}

public static class OtpPurposeIds
{
    public static readonly Guid PasswordReset = new("BD3B00CA-D8E3-4B4A-9B98-90E5B7F58B50");
    public static readonly Guid UsernameRecovery = new("2E8909F7-34EF-4E74-8479-7A4A2FEE9D93");
}

public static class TicketStatusIds
{
    public static readonly Guid Open = new("40000000-0000-0000-0000-000000000001");
    public static readonly Guid UnderReview = new("40000000-0000-0000-0000-000000000002");
    public static readonly Guid Rejected = new("40000000-0000-0000-0000-000000000004");
    public static readonly Guid Closed = new("40000000-0000-0000-0000-000000000005");
}

public static class TicketCategoryIds
{
    public static readonly Guid TransactionDispute = new("50000000-0000-0000-0000-000000000001");
    public static readonly Guid TransactionIssue = new("50000000-0000-0000-0000-000000000002");
    public static readonly Guid Security = new("50000000-0000-0000-0000-000000000003");
    public static readonly Guid General = new("50000000-0000-0000-0000-000000000004");
}

public static class TicketPriorityIds
{
    public static readonly Guid Low = new("60000000-0000-0000-0000-000000000001");
    public static readonly Guid Normal = new("60000000-0000-0000-0000-000000000002");
    public static readonly Guid High = new("60000000-0000-0000-0000-000000000003");
}
