using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;

namespace Banking_System.ApiService.Services;

public enum CreateTicketStatus
{
    Created,
    CustomerNotAllowed,
    InvalidCategory,
    TransactionNotFound
}

public enum TicketActionStatus
{
    Success,
    NotFound,
    InvalidTransition,
    Conflict
}

public sealed record CreateTicketResult(CreateTicketStatus Status, Guid? TicketId = null);

public sealed record TicketActionResult(TicketActionStatus Status);

/// <summary>
/// Support tickets: a customer raises one (optionally against one of their own
/// transactions), and an employee or admin works it through a small, fixed state
/// machine: Open -&gt; UnderReview -&gt; Resolved/Rejected -&gt; Closed. Every step is
/// recorded as a <see cref="TicketEvent"/>, which is also the whole activity
/// trail shown to both the customer and the employee.
/// </summary>
public sealed class SupportTicketService
{
    private static readonly IReadOnlySet<Guid> ValidCategories = new HashSet<Guid>
    {
        TicketCategoryIds.TransactionDispute,
        TicketCategoryIds.TransactionIssue,
        TicketCategoryIds.Security,
        TicketCategoryIds.General
    };

    private readonly IUserRepository _users;
    private readonly ITransactionRepository _transactions;
    private readonly ISupportTicketRepository _tickets;
    private readonly ILogger<SupportTicketService> _logger;

    public SupportTicketService(
        IUserRepository users,
        ITransactionRepository transactions,
        ISupportTicketRepository tickets,
        ILogger<SupportTicketService> logger)
    {
        _users = users;
        _transactions = transactions;
        _tickets = tickets;
        _logger = logger;
    }

    public async Task<CreateTicketResult> CreateAsync(
        Guid userId, CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);

        if (user is not { IsActive: true, BankCustomerId: { } customerId })
        {
            return new CreateTicketResult(CreateTicketStatus.CustomerNotAllowed);
        }

        if (!ValidCategories.Contains(request.CategoryId))
        {
            return new CreateTicketResult(CreateTicketStatus.InvalidCategory);
        }

        if (request.TransactionId is { } transactionId)
        {
            var transaction = await _transactions.GetByIdAsync(transactionId, cancellationToken);

            if (transaction is null
                || (transaction.FromCustomerId != customerId && transaction.ToCustomerId != customerId))
            {
                return new CreateTicketResult(CreateTicketStatus.TransactionNotFound);
            }
        }

        // An unauthorized/suspicious-transaction report is treated as high
        // priority automatically; everything else starts at normal priority.
        var priorityId = request.CategoryId == TicketCategoryIds.Security
            ? TicketPriorityIds.High
            : TicketPriorityIds.Normal;

        var ticket = new SupportTicket
        {
            TicketId = Guid.NewGuid(),
            CustomerId = customerId,
            CreatedByUserId = userId,
            CategoryId = request.CategoryId,
            PriorityId = priorityId,
            StatusId = TicketStatusIds.Open,
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            TransactionId = request.TransactionId,
            CreatedAt = DateTime.UtcNow
        };

        var creationEvent = new TicketEvent
        {
            TicketEventId = Guid.NewGuid(),
            TicketId = ticket.TicketId,
            ActorUserId = userId,
            FromStatusId = null,
            ToStatusId = TicketStatusIds.Open,
            Note = null,
            CreatedAt = ticket.CreatedAt
        };

        await _tickets.CreateAsync(ticket, creationEvent, cancellationToken);

        _logger.LogInformation("Ticket {TicketId} created by user {UserId}.", ticket.TicketId, userId);

        return new CreateTicketResult(CreateTicketStatus.Created, ticket.TicketId);
    }

    public Task<IReadOnlyList<SupportTicket>> GetForCustomerAsync(
        Guid customerId, Guid? statusId, CancellationToken cancellationToken) =>
        _tickets.GetForCustomerAsync(customerId, statusId, take: 200, cancellationToken);

    public Task<IReadOnlyList<SupportTicket>> GetQueueAsync(
        Guid? statusId, Guid? categoryId, CancellationToken cancellationToken) =>
        _tickets.GetQueueAsync(statusId, categoryId, take: 200, cancellationToken);

    /// <summary>Null if the ticket does not exist, or exists but does not belong to the caller (customers only see their own).</summary>
    public async Task<TicketDetail?> GetDetailAsync(
        Guid userId, bool isStaff, Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await _tickets.GetByIdAsync(ticketId, cancellationToken);

        if (ticket is null)
        {
            return null;
        }

        if (!isStaff)
        {
            var user = await _users.GetByIdAsync(userId, cancellationToken);

            if (user?.BankCustomerId != ticket.CustomerId)
            {
                return null;
            }
        }

        var events = await _tickets.GetEventsAsync(ticketId, cancellationToken);

        return new TicketDetail(
            ticket.TicketId,
            ticket.Category,
            ticket.Priority,
            ticket.Status,
            ticket.Subject,
            ticket.Description,
            ticket.TransactionId,
            ticket.AssignedToUsername,
            ticket.ResolutionNote,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            events.Select(e => new TicketEventDto(
                e.TicketEventId, e.ActorUsername, e.ActorRole, e.FromStatus, e.ToStatus, e.Note, e.CreatedAt)).ToList());
    }

    public Task<TicketActionResult> StartReviewAsync(Guid employeeUserId, Guid ticketId, CancellationToken cancellationToken) =>
        TransitionAsync(ticketId, [TicketStatusIds.Open], TicketStatusIds.UnderReview, employeeUserId, note: null, assign: true, resolutionNote: null, cancellationToken);

    public Task<TicketActionResult> ResolveAsync(Guid employeeUserId, Guid ticketId, string note, CancellationToken cancellationToken) =>
        TransitionAsync(ticketId, [TicketStatusIds.Open, TicketStatusIds.UnderReview], TicketStatusIds.Resolved, employeeUserId, note, assign: true, resolutionNote: note, cancellationToken);

    public Task<TicketActionResult> RejectAsync(Guid employeeUserId, Guid ticketId, string note, CancellationToken cancellationToken) =>
        TransitionAsync(ticketId, [TicketStatusIds.Open, TicketStatusIds.UnderReview], TicketStatusIds.Rejected, employeeUserId, note, assign: true, resolutionNote: note, cancellationToken);

    public Task<TicketActionResult> CloseAsync(Guid employeeUserId, Guid ticketId, string? note, CancellationToken cancellationToken) =>
        TransitionAsync(ticketId, [TicketStatusIds.Resolved, TicketStatusIds.Rejected], TicketStatusIds.Closed, employeeUserId, note, assign: false, resolutionNote: null, cancellationToken);

    private async Task<TicketActionResult> TransitionAsync(
        Guid ticketId,
        IReadOnlyList<Guid> allowedFrom,
        Guid toStatusId,
        Guid actorUserId,
        string? note,
        bool assign,
        string? resolutionNote,
        CancellationToken cancellationToken)
    {
        var ticket = await _tickets.GetByIdAsync(ticketId, cancellationToken);

        if (ticket is null)
        {
            return new TicketActionResult(TicketActionStatus.NotFound);
        }

        if (!allowedFrom.Contains(ticket.StatusId))
        {
            return new TicketActionResult(TicketActionStatus.InvalidTransition);
        }

        var applied = await _tickets.ApplyTransitionAsync(
            ticketId,
            ticket.StatusId,
            toStatusId,
            actorUserId,
            note,
            assign ? actorUserId : null,
            resolutionNote,
            cancellationToken);

        if (!applied)
        {
            // Someone else changed the ticket's status between the read above and now.
            return new TicketActionResult(TicketActionStatus.Conflict);
        }

        _logger.LogInformation(
            "Ticket {TicketId} moved to {Status} by user {UserId}.", ticketId, toStatusId, actorUserId);

        return new TicketActionResult(TicketActionStatus.Success);
    }
}
