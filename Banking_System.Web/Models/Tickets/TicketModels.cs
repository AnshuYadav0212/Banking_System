using System.ComponentModel.DataAnnotations;

namespace Banking_System.Web.Models.Tickets;

// Mirrors Banking_System.ApiService/Models/LookupIds.cs (TicketCategoryIds / TicketStatusIds).
// The database seeds exactly these GUIDs, so the two must stay in step.
public static class TicketCategoryIds
{
    public static readonly Guid TransactionDispute = new("5B03D8F1-E6A4-4297-9C5E-17A8B4D2F096");
    public static readonly Guid TransactionIssue = new("E2749A6C-0D81-4B35-A7F3-C96B2E80D1A5");
    public static readonly Guid Security = new("08C5F7A3-B19E-46D2-8E04-7D3A6F5B9C12");
    public static readonly Guid General = new("F61D2B84-5C97-4A03-B5E8-2A90C7E1D34F");
}

public static class TicketStatusIds
{
    public static readonly Guid Open = new("7C1E4A92-3B58-4D06-A1F7-E29D5C08B413");
    public static readonly Guid UnderReview = new("D48B60F3-91A2-4E7C-8D35-0F6A7B1C92E8");
    public static readonly Guid Rejected = new("A6250E8B-47D3-4F19-B3C8-51E9D07A2C64");
    public static readonly Guid Closed = new("91E7C3B0-2A5F-4D68-86B1-F4C0A93D5E27");
}

public static class TicketStatuses
{
    public static readonly IReadOnlyList<(string Code, Guid Id, string Label)> All =
    [
        ("Open", TicketStatusIds.Open, "Open"),
        ("UnderReview", TicketStatusIds.UnderReview, "Under Review"),
        ("Rejected", TicketStatusIds.Rejected, "Rejected"),
        ("Closed", TicketStatusIds.Closed, "Closed")
    ];

    public static Guid? ToId(string? code) =>
        All.Where(s => s.Code == code).Select(s => (Guid?)s.Id).FirstOrDefault();

    // "Resolved" is a retired status - no ticket can be set to it anymore, but old
    // activity-log entries can still reference it, so it keeps a readable label here.
    public static string Label(string code) => code switch
    {
        "Open" => "Open",
        "UnderReview" => "Under Review",
        "Resolved" => "Resolved",
        "Rejected" => "Rejected",
        "Closed" => "Closed",
        _ => code
    };

    public static string BadgeClass(string code) => code switch
    {
        "Open" => "text-bg-warning",
        "UnderReview" => "text-bg-info",
        "Resolved" => "text-bg-success",
        "Rejected" => "text-bg-danger",
        "Closed" => "text-bg-secondary",
        _ => "text-bg-secondary"
    };
}

public static class TicketCategories
{
    public static readonly IReadOnlyList<(Guid Id, string Label)> All =
    [
        (TicketCategoryIds.TransactionDispute, "Transaction Dispute"),
        (TicketCategoryIds.TransactionIssue, "Transaction Issue"),
        (TicketCategoryIds.Security, "Security / Unauthorized Transaction"),
        (TicketCategoryIds.General, "General")
    ];

    public static string Label(string code) => code switch
    {
        "TransactionDispute" => "Transaction Dispute",
        "TransactionIssue" => "Transaction Issue",
        "Security" => "Security / Unauthorized Transaction",
        "General" => "General",
        _ => code
    };

    // A dispute or an issue is inherently about one transaction, so the
    // related-transaction field stops being optional for these two.
    public static bool RequiresTransaction(Guid categoryId) =>
        categoryId == TicketCategoryIds.TransactionDispute || categoryId == TicketCategoryIds.TransactionIssue;
}

public sealed record TicketSummary(
    Guid TicketId,
    string Category,
    string Priority,
    string Status,
    string Subject,
    Guid? TransactionId,
    string? AssignedToUsername,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record TicketEventDto(
    Guid TicketEventId,
    string? ActorUsername,
    string ActorRole,
    string? FromStatus,
    string ToStatus,
    string? Note,
    DateTime CreatedAt);

public sealed record TicketDetail(
    Guid TicketId,
    string Category,
    string Priority,
    string Status,
    string Subject,
    string Description,
    Guid? TransactionId,
    string? AssignedToUsername,
    string? ResolutionNote,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<TicketEventDto> Events);

/// <summary>The raise-a-ticket form.</summary>
public sealed class CreateTicketInput
{
    public Guid CategoryId { get; set; } = TicketCategoryIds.General;

    [Required(ErrorMessage = "Enter a short subject.")]
    [StringLength(200, MinimumLength = 4, ErrorMessage = "Subject must be 4 to 200 characters.")]
    public string Subject { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Description must be at most 2000 characters.")]
    public string? Description { get; set; }

    public Guid? TransactionId { get; set; }
}

public sealed record CreateTicketRequest(Guid CategoryId, string Subject, string? Description, Guid? TransactionId);

public sealed record TicketActionRequest(string? Note);
