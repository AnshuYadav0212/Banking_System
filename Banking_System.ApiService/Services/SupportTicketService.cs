using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;

namespace Banking_System.ApiService.Services;

public enum CreateTicketStatus
{
    Created,
    CustomerNotAllowed,
    InvalidCategory,
    TransactionNotFound,
    TransactionRequired
}

public enum TicketActionStatus
{
    Success,
    NotFound,
    InvalidTransition,
    Conflict,
    LockedByAnotherEmployee
}

public sealed record CreateTicketResult(CreateTicketStatus Status, Guid? TicketId = null);

public sealed record TicketActionResult(TicketActionStatus Status);

/// <summary>
/// Support tickets: a customer raises one (optionally against one of their own
/// transactions), and an employee or admin works it through a small, fixed state
/// machine: Open -&gt; UnderReview -&gt; Rejected/Closed. Every step is recorded as a
/// <see cref="TicketEvent"/>, which is also the whole activity trail shown to
/// both the customer and the employee.
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

    // A dispute or an issue is inherently about one transaction; without it
    // there is nothing concrete for an employee to investigate.
    private static readonly IReadOnlySet<Guid> TransactionRequiredCategories = new HashSet<Guid>
    {
        TicketCategoryIds.TransactionDispute,
        TicketCategoryIds.TransactionIssue
    };

    // How long one employee's review lease lasts. While it runs, nobody else can
    // act on the ticket; once it lapses another employee may take the ticket over
    // (which locks the first one out). Bounded, so an abandoned review can never
    // block a ticket for good.
    private const int ReviewLockSeconds = 60;

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

        if (request.TransactionId is null && TransactionRequiredCategories.Contains(request.CategoryId))
        {
            return new CreateTicketResult(CreateTicketStatus.TransactionRequired);
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
            Description = request.Description?.Trim() ?? string.Empty,
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
            isStaff && IsLockedAgainst(ticket, userId),
            ticket.ReviewLockExpiresAt is { } lockEnd ? DateTime.SpecifyKind(lockEnd, DateTimeKind.Utc) : null,
            events.Select(e => new TicketEventDto(
                e.TicketEventId, e.ActorUsername, e.ActorRole, e.FromStatus, e.ToStatus, e.Note, e.CreatedAt)).ToList());
    }

    // Also allowed from UnderReview: the current holder renews the lease, or another
    // employee takes the ticket over once the holder's lease has lapsed.
    public Task<TicketActionResult> StartReviewAsync(Guid employeeUserId, Guid ticketId, CancellationToken cancellationToken) =>
        TransitionAsync(ticketId, [TicketStatusIds.Open, TicketStatusIds.UnderReview], TicketStatusIds.UnderReview, employeeUserId, note: null, assign: true, resolutionNote: null, cancellationToken);

    // Resolving a ticket closes it directly - there is no separate "Resolved"
    // status to move out of afterwards.
    public Task<TicketActionResult> ResolveAsync(Guid employeeUserId, Guid ticketId, string note, CancellationToken cancellationToken) =>
        TransitionAsync(ticketId, [TicketStatusIds.Open, TicketStatusIds.UnderReview], TicketStatusIds.Closed, employeeUserId, note, assign: true, resolutionNote: note, cancellationToken);

    public Task<TicketActionResult> RejectAsync(Guid employeeUserId, Guid ticketId, string note, CancellationToken cancellationToken) =>
        TransitionAsync(ticketId, [TicketStatusIds.Open, TicketStatusIds.UnderReview], TicketStatusIds.Rejected, employeeUserId, note, assign: true, resolutionNote: note, cancellationToken);

    private static bool IsLockedAgainst(SupportTicket ticket, Guid userId) =>
        ticket.StatusId == TicketStatusIds.UnderReview
        && ticket.AssignedToUserId is { } holder
        && holder != userId
        && ticket.ReviewLockExpiresAt is { } expires
        && DateTime.SpecifyKind(expires, DateTimeKind.Utc) > DateTime.UtcNow;

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

        // Same-status "transition" = renewing or taking over a review lease.
        if (ticket.StatusId == toStatusId)
        {
            note ??= ticket.AssignedToUserId == actorUserId ? "Review lease renewed." : "Took over the review.";
        }

        var outcome = await _tickets.ApplyTransitionAsync(
            ticketId,
            ticket.StatusId,
            toStatusId,
            actorUserId,
            note,
            assign ? actorUserId : null,
            resolutionNote,
            ReviewLockSeconds,
            cancellationToken);

        if (outcome == TransitionOutcome.LockedByAnotherEmployee)
        {
            return new TicketActionResult(TicketActionStatus.LockedByAnotherEmployee);
        }

        if (outcome != TransitionOutcome.Applied)
        {
            // Someone else changed the ticket's status between the read above and now.
            return new TicketActionResult(TicketActionStatus.Conflict);
        }

        _logger.LogInformation(
            "Ticket {TicketId} moved to {Status} by user {UserId}.", ticketId, toStatusId, actorUserId);

        return new TicketActionResult(TicketActionStatus.Success);
    }
}
