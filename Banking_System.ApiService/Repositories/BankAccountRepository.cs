using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Dapper;

namespace Banking_System.ApiService.Repositories;

public interface IBankAccountRepository
{
    Task<IReadOnlyList<BankAccount>> GetByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<BankAccount?> GetByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an account by its number, together with what decides whether it can receive a transfer.</summary>
    Task<RecipientLookup?> FindRecipientAsync(
        string accountNumber,
        CancellationToken cancellationToken = default);
}

public sealed class BankAccountRepository : IBankAccountRepository
{
    private const string SelectAccount = """
        SELECT AccountId, AccountNumber, CustomerId, AvailableBalance, StatusId
        FROM dbo.BankAccounts
        """;

    private readonly ISqlConnectionFactory _connectionFactory;

    public BankAccountRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<BankAccount>> GetByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var accounts = await connection.QueryAsync<BankAccount>(
            new CommandDefinition(
                SelectAccount + " WHERE CustomerId = @CustomerId ORDER BY AccountNumber;",
                new { CustomerId = customerId },
                cancellationToken: cancellationToken));

        return accounts.ToList();
    }

    public async Task<BankAccount?> GetByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<BankAccount>(
            new CommandDefinition(
                SelectAccount + " WHERE AccountId = @AccountId;",
                new { AccountId = accountId },
                cancellationToken: cancellationToken));
    }

    public async Task<RecipientLookup?> FindRecipientAsync(
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                a.AccountId,
                a.AccountNumber,
                a.CustomerId,
                a.StatusId AS AccountStatusId,
                c.StatusId AS CustomerStatusId,
                c.FirstName,
                c.LastName,
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM dbo.Users u
                    WHERE u.BankCustomerId = a.CustomerId
                      AND u.StatusId = @Active)
                THEN 1 ELSE 0 END AS HasActiveLogin
            FROM dbo.BankAccounts a
            JOIN dbo.BankCustomers c ON c.CustomerId = a.CustomerId
            WHERE a.AccountNumber = @AccountNumber;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<RecipientLookup>(
            new CommandDefinition(
                sql,
                new { AccountNumber = accountNumber, Active = StatusIds.Active },
                cancellationToken: cancellationToken));
    }
}
