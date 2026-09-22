using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Dapper;

namespace Banking_System.ApiService.Repositories;

public interface ISupportTicketRepository
{
    /// <summary>Creates the ticket and its first (creation) event in one transaction.</summary>
    Task CreateAsync(SupportTicket ticket, TicketEvent creationEvent, CancellationToken cancellationToken = default);

    Task<SupportTicket?> GetByIdAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupportTicket>> GetForCustomerAsync(
        Guid customerId, Guid? statusId, int take, CancellationToken cancellationToken = default);

    /// <summary>All tickets, for the employee queue. Newest first, unresolved ones effectively surfacing sooner via the status filter.</summary>
    Task<IReadOnlyList<SupportTicket>> GetQueueAsync(
        Guid? statusId, Guid? categoryId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketEvent>> GetEventsAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>Moves the ticket to a new status, optionally (re)assigning it, and records the change as an event. All in one transaction.</summary>
    Task<bool> ApplyTransitionAsync(
        Guid ticketId,
        Guid expectedCurrentStatusId,
        Guid newStatusId,
        Guid actorUserId,
        string? note,
        Guid? assignedToUserId,
        string? resolutionNote,
        CancellationToken cancellationToken = default);
}

public sealed class SupportTicketRepository : ISupportTicketRepository
{
    private const string SelectTicket = """
        SELECT
            t.TicketId,
            t.CustomerId,
            t.CreatedByUserId,
            t.CategoryId,
            cat.Name AS Category,
            t.PriorityId,
            pri.Name AS Priority,
            t.StatusId,
            st.Name AS Status,
            t.Subject,
            t.Description,
            t.TransactionId,
            t.AssignedToUserId,
            au.Username AS AssignedToUsername,
            t.ResolutionNote,
            t.CreatedAt,
            t.UpdatedAt
        FROM dbo.SupportTickets t
        JOIN dbo.TicketCategories cat ON cat.CategoryId = t.CategoryId
        JOIN dbo.TicketPriorities pri ON pri.PriorityId = t.PriorityId
        JOIN dbo.TicketStatuses st ON st.StatusId = t.StatusId
        LEFT JOIN dbo.Users au ON au.UserId = t.AssignedToUserId
        """;

    private const string SelectEvent = """
        SELECT
            e.TicketEventId,
            e.TicketId,
            e.ActorUserId,
            u.Username AS ActorUsername,
            r.Name AS ActorRole,
            e.FromStatusId,
            fs.Name AS FromStatus,
            e.ToStatusId,
            ts.Name AS ToStatus,
            e.Note,
            e.CreatedAt
        FROM dbo.TicketEvents e
        JOIN dbo.Users u ON u.UserId = e.ActorUserId
        JOIN dbo.Roles r ON r.RoleId = u.RoleId
        LEFT JOIN dbo.TicketStatuses fs ON fs.StatusId = e.FromStatusId
        JOIN dbo.TicketStatuses ts ON ts.StatusId = e.ToStatusId
        """;

    private readonly ISqlConnectionFactory _connectionFactory;

    public SupportTicketRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(
        SupportTicket ticket, TicketEvent creationEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.SupportTickets
                (TicketId, CustomerId, CreatedByUserId, CategoryId, PriorityId, StatusId,
                 Subject, Description, TransactionId, CreatedAt)
            VALUES
                (@TicketId, @CustomerId, @CreatedByUserId, @CategoryId, @PriorityId, @StatusId,
                 @Subject, @Description, @TransactionId, @CreatedAt);
            """,
            ticket, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.TicketEvents
                (TicketEventId, TicketId, ActorUserId, FromStatusId, ToStatusId, Note, CreatedAt)
            VALUES
                (@TicketEventId, @TicketId, @ActorUserId, @FromStatusId, @ToStatusId, @Note, @CreatedAt);
            """,
            creationEvent, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SupportTicket?> GetByIdAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<SupportTicket>(new CommandDefinition(
            SelectTicket + " WHERE t.TicketId = @TicketId;",
            new { TicketId = ticketId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SupportTicket>> GetForCustomerAsync(
        Guid customerId, Guid? statusId, int take, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var tickets = await connection.QueryAsync<SupportTicket>(new CommandDefinition(
            SelectTicket + """
             WHERE t.CustomerId = @CustomerId
               AND (@StatusId IS NULL OR t.StatusId = @StatusId)
            ORDER BY t.CreatedAt DESC
            OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY;
            """,
            new { CustomerId = customerId, StatusId = statusId, Take = take },
            cancellationToken: cancellationToken));

        return tickets.ToList();
    }

    public async Task<IReadOnlyList<SupportTicket>> GetQueueAsync(
        Guid? statusId, Guid? categoryId, int take, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var tickets = await connection.QueryAsync<SupportTicket>(new CommandDefinition(
            SelectTicket + """
             WHERE (@StatusId IS NULL OR t.StatusId = @StatusId)
               AND (@CategoryId IS NULL OR t.CategoryId = @CategoryId)
            ORDER BY
                CASE WHEN t.PriorityId = @High THEN 0 ELSE 1 END,
                t.CreatedAt DESC
            OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY;
            """,
            new { StatusId = statusId, CategoryId = categoryId, High = TicketPriorityIds.High, Take = take },
            cancellationToken: cancellationToken));

        return tickets.ToList();
    }

    public async Task<IReadOnlyList<TicketEvent>> GetEventsAsync(
        Guid ticketId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var events = await connection.QueryAsync<TicketEvent>(new CommandDefinition(
            SelectEvent + " WHERE e.TicketId = @TicketId ORDER BY e.CreatedAt;",
            new { TicketId = ticketId },
            cancellationToken: cancellationToken));

        return events.ToList();
    }

    public async Task<bool> ApplyTransitionAsync(
        Guid ticketId,
        Guid expectedCurrentStatusId,
        Guid newStatusId,
        Guid actorUserId,
        string? note,
        Guid? assignedToUserId,
        string? resolutionNote,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Only move the ticket if it is still in the status the caller last saw
        // (WITH (UPDLOCK) so two employees acting at once cannot both "win").
        var current = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT StatusId FROM dbo.SupportTickets WITH (UPDLOCK, ROWLOCK) WHERE TicketId = @TicketId;",
            new { TicketId = ticketId },
            transaction,
            cancellationToken: cancellationToken));

        if (current is null || current != expectedCurrentStatusId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.SupportTickets
            SET StatusId = @NewStatusId,
                AssignedToUserId = COALESCE(@AssignedToUserId, AssignedToUserId),
                ResolutionNote = COALESCE(@ResolutionNote, ResolutionNote),
                UpdatedAt = SYSUTCDATETIME()
            WHERE TicketId = @TicketId;
            """,
            new { TicketId = ticketId, NewStatusId = newStatusId, AssignedToUserId = assignedToUserId, ResolutionNote = resolutionNote },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.TicketEvents (TicketEventId, TicketId, ActorUserId, FromStatusId, ToStatusId, Note, CreatedAt)
            VALUES (@TicketEventId, @TicketId, @ActorUserId, @FromStatusId, @ToStatusId, @Note, @CreatedAt);
            """,
            new
            {
                TicketEventId = Guid.NewGuid(),
                TicketId = ticketId,
                ActorUserId = actorUserId,
                FromStatusId = expectedCurrentStatusId,
                ToStatusId = newStatusId,
                Note = note,
                CreatedAt = DateTime.UtcNow
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
