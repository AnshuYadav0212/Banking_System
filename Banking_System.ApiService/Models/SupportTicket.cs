namespace Banking_System.ApiService.Models;

/// <summary>
/// A customer's support ticket, optionally about one of their own transactions.
/// Names of lookup keys (Category, Priority, Status) are filled in by joins when read.
/// </summary>
public sealed class SupportTicket
{
    public Guid TicketId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Guid CategoryId { get; set; }

    public string Category { get; set; } = string.Empty;

    public Guid PriorityId { get; set; }

    public string Priority { get; set; } = string.Empty;

    public Guid StatusId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid? TransactionId { get; set; }

    public Guid? AssignedToUserId { get; set; }

    public string? AssignedToUsername { get; set; }

    public string? ResolutionNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

/// <summary>One entry in a ticket's activity trail: its creation, or a status change.</summary>
public sealed class TicketEvent
{
    public Guid TicketEventId { get; set; }

    public Guid TicketId { get; set; }

    public Guid ActorUserId { get; set; }

    public string? ActorUsername { get; set; }

    public string ActorRole { get; set; } = string.Empty;

    public Guid? FromStatusId { get; set; }

    public string? FromStatus { get; set; }

    public Guid ToStatusId { get; set; }

    public string ToStatus { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
