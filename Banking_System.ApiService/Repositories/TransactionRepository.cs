using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Banking_System.ApiService.Repositories;

public sealed record TransferCommand(
    Guid RequestId,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string? Comment,
    Guid InitiatedByUserId);

public enum TransferOutcomeStatus
{
    Completed,
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
            t.FromAccountId,
            t.ToAccountId,
            fa.AccountNumber AS FromAccountNumber,
            ta.AccountNumber AS ToAccountNumber,
            fa.CustomerId AS FromCustomerId,
            ta.CustomerId AS ToCustomerId,
            fc.FirstName AS FromFirstName,
            fc.LastName AS FromLastName,
            tc.FirstName AS ToFirstName,
            tc.LastName AS ToLastName
        FROM dbo.Transactions t
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

            var senderBalanceAfter = await connection.ExecuteScalarAsync<decimal>(
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

            var transactionId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO dbo.Transactions
                        (TransactionId, RequestId, FromAccountId, ToAccountId, Amount, Comment, InitiatedByUserId, CreatedAt)
                    VALUES
                        (@TransactionId, @RequestId, @FromAccountId, @ToAccountId, @Amount, @Comment, @InitiatedByUserId, @CreatedAt);
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
                        CreatedAt = createdAt
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            return new TransferOutcome(
                TransferOutcomeStatus.Completed,
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

    private sealed class LockedAccount
    {
        public Guid AccountId { get; set; }

        public decimal AvailableBalance { get; set; }

        public Guid StatusId { get; set; }
    }
}
