using System.ComponentModel.DataAnnotations;

namespace Banking_System.ApiService.DTOs;

/// <summary>Raise a new support ticket, optionally about one of the caller's own transactions.</summary>
public sealed record CreateTicketRequest(
    Guid CategoryId,
    [Required, StringLength(200, MinimumLength = 4)] string Subject,
    [Required, StringLength(2000, MinimumLength = 10)] string Description,
    Guid? TransactionId);

public sealed record TicketActionRequest(
    [StringLength(2000)] string? Note);

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
