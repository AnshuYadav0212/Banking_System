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
    public static readonly Guid Open = new("7C1E4A92-3B58-4D06-A1F7-E29D5C08B413");
    public static readonly Guid UnderReview = new("D48B60F3-91A2-4E7C-8D35-0F6A7B1C92E8");
    public static readonly Guid Rejected = new("A6250E8B-47D3-4F19-B3C8-51E9D07A2C64");
    public static readonly Guid Closed = new("91E7C3B0-2A5F-4D68-86B1-F4C0A93D5E27");
}

public static class TicketCategoryIds
{
    public static readonly Guid TransactionDispute = new("5B03D8F1-E6A4-4297-9C5E-17A8B4D2F096");
    public static readonly Guid TransactionIssue = new("E2749A6C-0D81-4B35-A7F3-C96B2E80D1A5");
    public static readonly Guid Security = new("08C5F7A3-B19E-46D2-8E04-7D3A6F5B9C12");
    public static readonly Guid General = new("F61D2B84-5C97-4A03-B5E8-2A90C7E1D34F");
}

public static class TransactionStatusIds
{
    public static readonly Guid Pending = new("2C7E91B4-58D3-4A06-B9F2-D1A8E60C3745");
    public static readonly Guid Processing = new("9F14A6D8-03BE-47C5-8A21-6E5B7D90C3F1");
    public static readonly Guid Completed = new("E5B30C72-A9D4-4F68-91E7-08C4D2A6B5F3");
    public static readonly Guid Rejected = new("6D82F5A1-C7E0-4B39-A4D6-3F19B8E20C74");
}

public static class TicketPriorityIds
{
    public static readonly Guid Low = new("4A8E19C7-D0F3-4B62-97A5-6E2B8C3F1D80");
    public static readonly Guid Normal = new("B37F5D02-8A64-4E91-A0C9-93D1E7B4F5A2");
    public static readonly Guid High = new("69D0A4E5-F2B7-4C18-8B36-C5E90A7D2F41");
}
