using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data.Common;

namespace Banking_System.ApiService.Repositories;

public sealed record TransferCommand(
    Guid RequestId,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string? Comment,
    Guid InitiatedByUserId,
    bool RequiresApproval = false);

public enum TransferOutcomeStatus
{
    Completed,
    /// <summary>Validated and recorded as Pending; no money has moved yet.</summary>
    PendingApproval,
    InsufficientFunds,
    AccountInactive,
    AccountNotFound,
    DuplicateRequest
}

public sealed record TransferOutcome(
    TransferOutcomeStatus Status,
    Guid? TransactionId = null,
    DateTime? CreatedAt = null,
    decimal? SenderBalanceAfter = null);

public sealed record TransactionStatusCounts(
    int PendingCount, decimal PendingTotal, int ProcessingCount, int CompletedCount, int RejectedCount);

public enum TransactionReviewOutcome
{
    Applied,
    StatusChanged,
    LockedByAnotherEmployee,
    InsufficientFunds,
    AccountInactive
}

public interface ITransactionRepository
{
    /// <summary>
    /// Moves the money and records the transfer, all or nothing. Both accounts are
    /// locked and re-checked inside the database transaction, so the outcome is
    /// correct however many transfers run at the same time.
    /// </summary>
    Task<TransferOutcome> TransferAsync(
        TransferCommand command,
        CancellationToken cancellationToken = default);

    Task<TransactionRecord?> GetByRequestIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<TransactionRecord?> GetByIdAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>The most recent transfers sent or received by any account of the customer.</summary>
    Task<IReadOnlyList<TransactionRecord>> GetForCustomerAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Every transaction, for the employee review queue.</summary>
    Task<IReadOnlyList<TransactionQueueEntry>> SearchQueueAsync(
        string? search, string? status, DateTime? from, DateTime? to, int page, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>All-time counts by status, for the queue page's summary chips (independent of the current filters).</summary>
    Task<TransactionStatusCounts> GetStatusCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Full detail for the employee review page, including both accounts' current balances.</summary>
    Task<TransactionReviewDetailEntry?> GetReviewDetailAsync(
        Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Pending -&gt; Processing (or Processing -&gt; Processing, to renew/take over the review lease).</summary>
    Task<TransactionReviewOutcome> StartReviewAsync(
        Guid transactionId, Guid actorUserId, int lockSeconds, CancellationToken cancellationToken = default);

    /// <summary>Processing -&gt; Completed. Re-checks and moves the money, all inside one locked transaction.</summary>
    Task<TransactionReviewOutcome> ApproveAsync(
        Guid transactionId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Processing -&gt; Rejected. No money has moved for a Pending/Processing transfer, so none moves back.</summary>
    Task<TransactionReviewOutcome> RejectAsync(
        Guid transactionId, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class TransactionRepository : ITransactionRepository
{
    private const string SelectRecord = """
        SELECT
            t.TransactionId,
            t.RequestId,
            t.CreatedAt,
            t.Amount,
            t.Comment,
            t.InitiatedByUserId,
            ts.Name AS Status,
            t.FromAccountId,
            t.ToAccountId,
            fa.AccountNumber AS FromAccountNumber,
            ta.AccountNumber AS ToAccountNumber,
            fa.CustomerId AS FromCustomerId,
            ta.CustomerId AS ToCustomerId,
            fc.FirstName AS FromFirstName,
            fc.LastName AS FromLastName,
            tc.FirstName AS ToFirstName,
            tc.LastName AS ToLastName,
            t.AssignedToUserId,
            au.Username AS AssignedToUsername,
            t.ReviewLockExpiresAt
        FROM dbo.Transactions t
        LEFT JOIN dbo.Users au ON au.UserId = t.AssignedToUserId
        JOIN dbo.TransactionStatuses ts ON ts.StatusId = t.StatusId
        JOIN dbo.BankAccounts fa ON fa.AccountId = t.FromAccountId
        JOIN dbo.BankAccounts ta ON ta.AccountId = t.ToAccountId
        JOIN dbo.BankCustomers fc ON fc.CustomerId = fa.CustomerId
        JOIN dbo.BankCustomers tc ON tc.CustomerId = ta.CustomerId
        """;

    private readonly ISqlConnectionFactory _connectionFactory;

    public TransactionRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TransferOutcome> TransferAsync(
        TransferCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Lock both accounts in one fixed order. Two transfers going in
            // opposite directions between the same pair then wait for each
            // other instead of deadlocking.
            var locked = new Dictionary<Guid, LockedAccount>();

            foreach (var accountId in new[] { command.FromAccountId, command.ToAccountId }.OrderBy(id => id))
            {
                var row = await connection.QuerySingleOrDefaultAsync<LockedAccount>(
                    new CommandDefinition(
                        "SELECT AccountId, AvailableBalance, StatusId FROM dbo.BankAccounts WITH (UPDLOCK, ROWLOCK) WHERE AccountId = @AccountId;",
                        new { AccountId = accountId },
                        transaction,
                        cancellationToken: cancellationToken));

                if (row is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new TransferOutcome(TransferOutcomeStatus.AccountNotFound);
                }

                locked[accountId] = row;
            }

            var from = locked[command.FromAccountId];
            var to = locked[command.ToAccountId];

            if (from.StatusId != StatusIds.Active || to.StatusId != StatusIds.Active)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new TransferOutcome(TransferOutcomeStatus.AccountInactive);
            }

            if (from.AvailableBalance < command.Amount)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new TransferOutcome(TransferOutcomeStatus.InsufficientFunds);
            }

            // A transfer that needs approval only records the request: no money moves
            // until an employee approves it, so the balance stays as it is.
            var senderBalanceAfter = from.AvailableBalance;

            if (!command.RequiresApproval)
            {
                senderBalanceAfter = await connection.ExecuteScalarAsync<decimal>(
                    new CommandDefinition(
                        """
                        UPDATE dbo.BankAccounts
                        SET AvailableBalance = AvailableBalance - @Amount
                        OUTPUT inserted.AvailableBalance
                        WHERE AccountId = @AccountId;
                        """,
                        new { Amount = command.Amount, AccountId = command.FromAccountId },
                        transaction,
                        cancellationToken: cancellationToken));

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE dbo.BankAccounts SET AvailableBalance = AvailableBalance + @Amount WHERE AccountId = @AccountId;",
                        new { Amount = command.Amount, AccountId = command.ToAccountId },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            var transactionId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO dbo.Transactions
                        (TransactionId, RequestId, FromAccountId, ToAccountId, Amount, Comment, InitiatedByUserId, StatusId, CreatedAt)
                    VALUES
                        (@TransactionId, @RequestId, @FromAccountId, @ToAccountId, @Amount, @Comment, @InitiatedByUserId, @StatusId, @CreatedAt);
                    """,
                    new
                    {
                        TransactionId = transactionId,
                        command.RequestId,
                        command.FromAccountId,
                        command.ToAccountId,
                        command.Amount,
                        command.Comment,
                        command.InitiatedByUserId,
                        StatusId = command.RequiresApproval ? TransactionStatusIds.Pending : TransactionStatusIds.Completed,
                        CreatedAt = createdAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            return new TransferOutcome(
                command.RequiresApproval ? TransferOutcomeStatus.PendingApproval : TransferOutcomeStatus.Completed,
                transactionId,
                createdAt,
                senderBalanceAfter);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627
            && ex.Message.Contains("UQ_Transactions_RequestId", StringComparison.OrdinalIgnoreCase))
        {
            // The same form was submitted twice at once; the other one won and
            // this whole transaction (including the debit) is rolled back.
            await transaction.RollbackAsync(CancellationToken.None);
            return new TransferOutcome(TransferOutcomeStatus.DuplicateRequest);
        }
    }

    public async Task<TransactionRecord?> GetByRequestIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<TransactionRecord>(
            new CommandDefinition(
                SelectRecord + " WHERE t.RequestId = @RequestId;",
                new { RequestId = requestId },
                cancellationToken: cancellationToken));
    }

    public async Task<TransactionRecord?> GetByIdAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<TransactionRecord>(
            new CommandDefinition(
                SelectRecord + " WHERE t.TransactionId = @TransactionId;",
                new { TransactionId = transactionId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TransactionRecord>> GetForCustomerAsync(
        Guid customerId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var records = await connection.QueryAsync<TransactionRecord>(
            new CommandDefinition(
                "SELECT TOP (@Take) * FROM (" + SelectRecord
                + " WHERE fa.CustomerId = @CustomerId OR ta.CustomerId = @CustomerId) x ORDER BY CreatedAt DESC;",
                new { Take = take, CustomerId = customerId },
                cancellationToken: cancellationToken));

        return records.ToList();
    }

    public async Task<IReadOnlyList<TransactionQueueEntry>> SearchQueueAsync(
        string? search, string? status, DateTime? from, DateTime? to, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<TransactionQueueEntry>(new CommandDefinition(
            """
            SELECT
                t.TransactionId,
                t.CreatedAt,
                t.Amount,
                t.Comment,
                ts.Name AS Status,
                fa.AccountNumber AS FromAccountNumber,
                ta.AccountNumber AS ToAccountNumber,
                fc.FirstName + N' ' + fc.LastName AS FromCustomerName,
                tc.FirstName + N' ' + tc.LastName AS ToCustomerName,
                iu.Username AS InitiatedByUsername,
                au.Username AS AssignedToUsername,
                t.AssignedToUserId,
                t.ReviewLockExpiresAt,
                COUNT(*) OVER () AS TotalCount
            FROM dbo.Transactions t
            JOIN dbo.TransactionStatuses ts ON ts.StatusId = t.StatusId
            JOIN dbo.BankAccounts fa ON fa.AccountId = t.FromAccountId
            JOIN dbo.BankAccounts ta ON ta.AccountId = t.ToAccountId
            JOIN dbo.BankCustomers fc ON fc.CustomerId = fa.CustomerId
            JOIN dbo.BankCustomers tc ON tc.CustomerId = ta.CustomerId
            LEFT JOIN dbo.Users iu ON iu.UserId = t.InitiatedByUserId
            LEFT JOIN dbo.Users au ON au.UserId = t.AssignedToUserId
            WHERE (@Like IS NULL
                   OR CONVERT(NVARCHAR(36), t.TransactionId) LIKE @Like ESCAPE N'\'
                   OR fa.AccountNumber LIKE @Like ESCAPE N'\'
                   OR ta.AccountNumber LIKE @Like ESCAPE N'\'
                   OR fc.FirstName + N' ' + fc.LastName LIKE @Like ESCAPE N'\'
                   OR tc.FirstName + N' ' + tc.LastName LIKE @Like ESCAPE N'\')
              AND (@Status IS NULL OR ts.Name = @Status)
              AND (@From IS NULL OR t.CreatedAt >= @From)
              AND (@To IS NULL OR t.CreatedAt < @To)
            ORDER BY t.CreatedAt DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """,
            new
            {
                Like = ToLikePattern(search),
                Status = status,
                From = from,
                To = to,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<TransactionStatusCounts> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var rows = (await connection.QueryAsync<(string Name, int Count, decimal Total)>(new CommandDefinition(
            """
            SELECT ts.Name, COUNT(*) AS Count, SUM(t.Amount) AS Total
            FROM dbo.Transactions t
            JOIN dbo.TransactionStatuses ts ON ts.StatusId = t.StatusId
            GROUP BY ts.Name;
            """,
            cancellationToken: cancellationToken))).ToDictionary(r => r.Name, r => r);

        int CountOf(string name) => rows.TryGetValue(name, out var r) ? r.Count : 0;
        decimal TotalOf(string name) => rows.TryGetValue(name, out var r) ? r.Total : 0m;

        return new TransactionStatusCounts(
            CountOf("Pending"), TotalOf("Pending"), CountOf("Processing"), CountOf("Completed"), CountOf("Rejected"));
    }

    public async Task<TransactionReviewDetailEntry?> GetReviewDetailAsync(
        Guid transactionId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<TransactionReviewDetailEntry>(new CommandDefinition(
            """
            SELECT
                t.TransactionId,
                t.CreatedAt,
                t.Amount,
                t.Comment,
                ts.Name AS Status,
                fa.AccountNumber AS FromAccountNumber,
                fc.FirstName + N' ' + fc.LastName AS FromCustomerName,
                fa.AvailableBalance AS FromAccountBalance,
                fTotal.TotalBalance AS FromCustomerTotalBalance,
                CAST(CASE WHEN fa.StatusId = @Active THEN 1 ELSE 0 END AS bit) AS FromAccountActive,
                CAST(CASE WHEN fc.StatusId = @Active THEN 1 ELSE 0 END AS bit) AS FromCustomerActive,
                ta.AccountNumber AS ToAccountNumber,
                tc.FirstName + N' ' + tc.LastName AS ToCustomerName,
                ta.AvailableBalance AS ToAccountBalance,
                tTotal.TotalBalance AS ToCustomerTotalBalance,
                CAST(CASE WHEN ta.StatusId = @Active THEN 1 ELSE 0 END AS bit) AS ToAccountActive,
                CAST(CASE WHEN tc.StatusId = @Active THEN 1 ELSE 0 END AS bit) AS ToCustomerActive,
                iu.Username AS InitiatedByUsername,
                t.AssignedToUserId,
                au.Username AS AssignedToUsername,
                t.ReviewLockExpiresAt
            FROM dbo.Transactions t
            JOIN dbo.TransactionStatuses ts ON ts.StatusId = t.StatusId
            JOIN dbo.BankAccounts fa ON fa.AccountId = t.FromAccountId
            JOIN dbo.BankAccounts ta ON ta.AccountId = t.ToAccountId
            JOIN dbo.BankCustomers fc ON fc.CustomerId = fa.CustomerId
            JOIN dbo.BankCustomers tc ON tc.CustomerId = ta.CustomerId
            LEFT JOIN dbo.Users iu ON iu.UserId = t.InitiatedByUserId
            LEFT JOIN dbo.Users au ON au.UserId = t.AssignedToUserId
            CROSS APPLY (SELECT SUM(b.AvailableBalance) AS TotalBalance FROM dbo.BankAccounts b WHERE b.CustomerId = fc.CustomerId) fTotal
            CROSS APPLY (SELECT SUM(b.AvailableBalance) AS TotalBalance FROM dbo.BankAccounts b WHERE b.CustomerId = tc.CustomerId) tTotal
            WHERE t.TransactionId = @TransactionId;
            """,
            new { TransactionId = transactionId, Active = StatusIds.Active },
            cancellationToken: cancellationToken));
    }

    public async Task<TransactionReviewOutcome> StartReviewAsync(
        Guid transactionId, Guid actorUserId, int lockSeconds, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await ReadLockedStatusAsync(connection, transaction, transactionId, actorUserId, cancellationToken);

        if (current is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.StatusChanged;
        }

        if (current.LockedByOther)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.LockedByAnotherEmployee;
        }

        if (current.StatusId != TransactionStatusIds.Pending && current.StatusId != TransactionStatusIds.Processing)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.StatusChanged;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Transactions
            SET StatusId = @Processing,
                AssignedToUserId = @ActorUserId,
                ReviewLockExpiresAt = DATEADD(SECOND, @LockSeconds, SYSUTCDATETIME())
            WHERE TransactionId = @TransactionId;
            """,
            new
            {
                TransactionId = transactionId,
                ActorUserId = actorUserId,
                Processing = TransactionStatusIds.Processing,
                LockSeconds = lockSeconds
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return TransactionReviewOutcome.Applied;
    }

    public async Task<TransactionReviewOutcome> ApproveAsync(
        Guid transactionId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await ReadLockedStatusAsync(connection, transaction, transactionId, actorUserId, cancellationToken);

        if (current is null || current.StatusId != TransactionStatusIds.Processing)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.StatusChanged;
        }

        if (current.LockedByOther)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.LockedByAnotherEmployee;
        }

        // Approval moves real money: the balance may have changed since the
        // transfer was first recorded (other transfers, other pending approvals),
        // so both accounts are locked and re-checked here, exactly as a normal
        // transfer would be.
        var locked = new Dictionary<Guid, LockedAccount>();

        foreach (var accountId in new[] { current.FromAccountId, current.ToAccountId }.OrderBy(id => id))
        {
            var row = await connection.QuerySingleOrDefaultAsync<LockedAccount>(new CommandDefinition(
                "SELECT AccountId, AvailableBalance, StatusId FROM dbo.BankAccounts WITH (UPDLOCK, ROWLOCK) WHERE AccountId = @AccountId;",
                new { AccountId = accountId },
                transaction,
                cancellationToken: cancellationToken));

            if (row is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return TransactionReviewOutcome.AccountInactive;
            }

            locked[accountId] = row;
        }

        var from = locked[current.FromAccountId];
        var to = locked[current.ToAccountId];

        if (from.StatusId != StatusIds.Active || to.StatusId != StatusIds.Active)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.AccountInactive;
        }

        if (from.AvailableBalance < current.Amount)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.InsufficientFunds;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.BankAccounts SET AvailableBalance = AvailableBalance - @Amount WHERE AccountId = @AccountId;",
            new { Amount = current.Amount, AccountId = current.FromAccountId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.BankAccounts SET AvailableBalance = AvailableBalance + @Amount WHERE AccountId = @AccountId;",
            new { Amount = current.Amount, AccountId = current.ToAccountId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Transactions
            SET StatusId = @Completed,
                AssignedToUserId = @ActorUserId,
                ReviewLockExpiresAt = NULL
            WHERE TransactionId = @TransactionId;
            """,
            new { TransactionId = transactionId, ActorUserId = actorUserId, Completed = TransactionStatusIds.Completed },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return TransactionReviewOutcome.Applied;
    }

    public async Task<TransactionReviewOutcome> RejectAsync(
        Guid transactionId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await ReadLockedStatusAsync(connection, transaction, transactionId, actorUserId, cancellationToken);

        if (current is null || current.StatusId != TransactionStatusIds.Processing)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.StatusChanged;
        }

        if (current.LockedByOther)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TransactionReviewOutcome.LockedByAnotherEmployee;
        }

        // Pending/Processing transfers never moved any money, so rejecting one
        // needs no balance change - only the status is recorded.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Transactions
            SET StatusId = @Rejected,
                AssignedToUserId = @ActorUserId,
                ReviewLockExpiresAt = NULL
            WHERE TransactionId = @TransactionId;
            """,
            new { TransactionId = transactionId, ActorUserId = actorUserId, Rejected = TransactionStatusIds.Rejected },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return TransactionReviewOutcome.Applied;
    }

    private static async Task<LockedReviewState?> ReadLockedStatusAsync(
        SqlConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<LockedReviewState>(new CommandDefinition(
            """
            SELECT
                t.StatusId,
                t.FromAccountId,
                t.ToAccountId,
                t.Amount,
                CAST(CASE WHEN t.AssignedToUserId IS NOT NULL
                          AND t.AssignedToUserId <> @ActorUserId
                          AND t.ReviewLockExpiresAt > SYSUTCDATETIME()
                          AND t.StatusId = @Processing
                         THEN 1 ELSE 0 END AS bit) AS LockedByOther
            FROM dbo.Transactions t WITH (UPDLOCK, ROWLOCK)
            WHERE t.TransactionId = @TransactionId;
            """,
            new { TransactionId = transactionId, ActorUserId = actorUserId, Processing = TransactionStatusIds.Processing },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static string? ToLikePattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = search.Trim()
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");

        return "%" + escaped + "%";
    }

    private sealed class LockedReviewState
    {
        public Guid StatusId { get; set; }

        public Guid FromAccountId { get; set; }

        public Guid ToAccountId { get; set; }

        public decimal Amount { get; set; }

        public bool LockedByOther { get; set; }
    }

    private sealed class LockedAccount
    {
        public Guid AccountId { get; set; }

        public decimal AvailableBalance { get; set; }

        public Guid StatusId { get; set; }
    }
}
