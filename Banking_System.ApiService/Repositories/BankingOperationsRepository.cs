using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Dapper;

namespace Banking_System.ApiService.Repositories;

public sealed class AccountOperationEntry
{
    public Guid AccountId { get; set; }

    public string AccountNumber { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string CifNumber { get; set; } = string.Empty;

    public decimal AvailableBalance { get; set; }

    public string Status { get; set; } = string.Empty;
}

public sealed class CustomerOperationEntry
{
    public Guid CustomerId { get; set; }

    public string CifNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int AccountCount { get; set; }
}

/// <summary>
/// The two staff actions that directly restrict banking access: freezing one
/// account, and restricting a customer's whole profile (which the login,
/// transfer and card flows already check via BankAccounts/BankCustomers.StatusId).
/// </summary>
public interface IBankingOperationsRepository
{
    Task<IReadOnlyList<AccountOperationEntry>> SearchAccountsAsync(
        string search, int take, CancellationToken cancellationToken = default);

    /// <summary>Returns false if the account does not exist.</summary>
    Task<bool> SetAccountStatusAsync(
        Guid accountId, bool active, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerOperationEntry>> SearchCustomersAsync(
        string search, int take, CancellationToken cancellationToken = default);

    /// <summary>Returns false if the customer does not exist.</summary>
    Task<bool> SetCustomerStatusAsync(
        Guid customerId, bool active, CancellationToken cancellationToken = default);
}

public sealed class BankingOperationsRepository : IBankingOperationsRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public BankingOperationsRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AccountOperationEntry>> SearchAccountsAsync(
        string search, int take, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<AccountOperationEntry>(new CommandDefinition(
            """
            SELECT TOP (@Take)
                a.AccountId,
                a.AccountNumber,
                c.FirstName + N' ' + c.LastName AS CustomerName,
                c.CifNumber,
                a.AvailableBalance,
                s.Name AS Status
            FROM dbo.BankAccounts a
            JOIN dbo.BankCustomers c ON c.CustomerId = a.CustomerId
            JOIN dbo.Statuses s ON s.StatusId = a.StatusId
            WHERE a.AccountNumber LIKE @Like ESCAPE N'\'
               OR c.CifNumber LIKE @Like ESCAPE N'\'
               OR c.FirstName + N' ' + c.LastName LIKE @Like ESCAPE N'\'
            ORDER BY c.LastName, c.FirstName, a.AccountNumber;
            """,
            new { Like = ToLikePattern(search), Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> SetAccountStatusAsync(
        Guid accountId, bool active, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.BankAccounts SET StatusId = @StatusId WHERE AccountId = @AccountId;",
            new { AccountId = accountId, StatusId = active ? StatusIds.Active : StatusIds.Inactive },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<IReadOnlyList<CustomerOperationEntry>> SearchCustomersAsync(
        string search, int take, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<CustomerOperationEntry>(new CommandDefinition(
            """
            SELECT TOP (@Take)
                c.CustomerId,
                c.CifNumber,
                c.FirstName + N' ' + c.LastName AS Name,
                c.PhoneNumber,
                s.Name AS Status,
                ISNULL(a.AccountCount, 0) AS AccountCount
            FROM dbo.BankCustomers c
            JOIN dbo.Statuses s ON s.StatusId = c.StatusId
            OUTER APPLY (SELECT COUNT(*) AS AccountCount FROM dbo.BankAccounts b WHERE b.CustomerId = c.CustomerId) a
            WHERE c.CifNumber LIKE @Like ESCAPE N'\'
               OR c.PhoneNumber LIKE @Like ESCAPE N'\'
               OR c.FirstName + N' ' + c.LastName LIKE @Like ESCAPE N'\'
            ORDER BY c.LastName, c.FirstName;
            """,
            new { Like = ToLikePattern(search), Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<bool> SetCustomerStatusAsync(
        Guid customerId, bool active, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        // Restricting a customer's profile is the security backstop: it blocks
        // login, transfers and new accounts everywhere those already check
        // BankCustomers.StatusId - no per-flow change needed. Restoring also
        // clears any automatic verification lockout, so the customer is not
        // left blocked by a stale LockedUntil after being manually cleared.
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.BankCustomers
            SET StatusId = @StatusId,
                LockedUntil = CASE WHEN @StatusId = @Active THEN NULL ELSE LockedUntil END,
                FailedVerificationCount = CASE WHEN @StatusId = @Active THEN 0 ELSE FailedVerificationCount END
            WHERE CustomerId = @CustomerId;
            """,
            new { CustomerId = customerId, StatusId = active ? StatusIds.Active : StatusIds.Inactive, Active = StatusIds.Active },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    private static string ToLikePattern(string search)
    {
        var escaped = search.Trim()
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");

        return "%" + escaped + "%";
    }
}
