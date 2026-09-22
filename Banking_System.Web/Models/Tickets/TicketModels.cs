using System.ComponentModel.DataAnnotations;

namespace Banking_System.Web.Models.Tickets;

// Mirrors Banking_System.ApiService/Models/LookupIds.cs (TicketCategoryIds / TicketStatusIds).
// The database seeds exactly these GUIDs, so the two must stay in step.
public static class TicketCategoryIds
{
    public static readonly Guid TransactionDispute = new("50000000-0000-0000-0000-000000000001");
    public static readonly Guid TransactionIssue = new("50000000-0000-0000-0000-000000000002");
    public static readonly Guid Security = new("50000000-0000-0000-0000-000000000003");
    public static readonly Guid General = new("50000000-0000-0000-0000-000000000004");
}

public static class TicketStatusIds
{
    public static readonly Guid Open = new("40000000-0000-0000-0000-000000000001");
    public static readonly Guid UnderReview = new("40000000-0000-0000-0000-000000000002");
    public static readonly Guid Resolved = new("40000000-0000-0000-0000-000000000003");
    public static readonly Guid Rejected = new("40000000-0000-0000-0000-000000000004");
    public static readonly Guid Closed = new("40000000-0000-0000-0000-000000000005");
}

public static class TicketStatuses
{
    public static readonly IReadOnlyList<(string Code, Guid Id, string Label)> All =
    [
        ("Open", TicketStatusIds.Open, "Open"),
        ("UnderReview", TicketStatusIds.UnderReview, "Under Review"),
        ("Resolved", TicketStatusIds.Resolved, "Resolved"),
        ("Rejected", TicketStatusIds.Rejected, "Rejected"),
        ("Closed", TicketStatusIds.Closed, "Closed")
    ];

    public static Guid? ToId(string? code) =>
        All.Where(s => s.Code == code).Select(s => (Guid?)s.Id).FirstOrDefault();

    public static string Label(string code) =>
        All.Where(s => s.Code == code).Select(s => s.Label).FirstOrDefault() ?? code;

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

    [Required(ErrorMessage = "Describe the issue.")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Description must be 10 to 2000 characters.")]
    public string Description { get; set; } = string.Empty;

    public Guid? TransactionId { get; set; }
}

public sealed record CreateTicketRequest(Guid CategoryId, string Subject, string Description, Guid? TransactionId);

public sealed record TicketActionRequest(string? Note);
