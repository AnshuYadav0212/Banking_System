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

public static class TicketStatusIds
{
    public static readonly Guid Open = new("40000000-0000-0000-0000-000000000001");
    public static readonly Guid UnderReview = new("40000000-0000-0000-0000-000000000002");
    public static readonly Guid Resolved = new("40000000-0000-0000-0000-000000000003");
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
