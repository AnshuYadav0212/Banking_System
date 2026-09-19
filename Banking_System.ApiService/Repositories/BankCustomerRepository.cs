using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Dapper;

namespace Banking_System.ApiService.Repositories;

public interface IBankCustomerRepository
{
    /// <summary>Finds a customer by CIF number or by any of their account numbers.</summary>
    Task<BankCustomer?> FindByAccountOrCifAsync(
        string accountOrCif,
        CancellationToken cancellationToken = default);

    Task<BankCustomer?> GetByIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task RecordFailedVerificationAsync(
        Guid customerId,
        int maxFailures,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);

    Task ResetFailedVerificationsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}

public sealed class BankCustomerRepository : IBankCustomerRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public BankCustomerRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<BankCustomer?> FindByAccountOrCifAsync(
        string accountOrCif,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1)
                c.CustomerId,
                c.CifNumber,
                c.FirstName,
                c.LastName,
                c.DateOfBirth,
                c.NationalId,
                c.PhoneNumber,
                c.StatusId,
                c.FailedVerificationCount,
                c.LockedUntil
            FROM dbo.BankCustomers c
            WHERE c.CifNumber = @Id
               OR EXISTS (
                    SELECT 1
                    FROM dbo.BankAccounts a
                    WHERE a.CustomerId = c.CustomerId
                      AND a.AccountNumber = @Id
                      AND a.StatusId = @Active);
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QueryFirstOrDefaultAsync<BankCustomer>(
            new CommandDefinition(
                sql,
                new { Id = accountOrCif, Active = StatusIds.Active },
                cancellationToken: cancellationToken));
    }

    public async Task<BankCustomer?> GetByIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CustomerId, CifNumber, FirstName, LastName, DateOfBirth,
                   NationalId, PhoneNumber, StatusId,
                   FailedVerificationCount, LockedUntil
            FROM dbo.BankCustomers
            WHERE CustomerId = @CustomerId;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<BankCustomer>(
            new CommandDefinition(
                sql,
                new { CustomerId = customerId },
                cancellationToken: cancellationToken));
    }

    public async Task RecordFailedVerificationAsync(
        Guid customerId,
        int maxFailures,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        // A single statement so concurrent guesses cannot slip past the limit.
        const string sql = """
            UPDATE dbo.BankCustomers
            SET FailedVerificationCount = FailedVerificationCount + 1,
                LockedUntil = CASE
                    WHEN FailedVerificationCount + 1 >= @MaxFailures
                        THEN DATEADD(SECOND, @LockSeconds, SYSUTCDATETIME())
                    ELSE LockedUntil END
            WHERE CustomerId = @CustomerId;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    CustomerId = customerId,
                    MaxFailures = maxFailures,
                    LockSeconds = (int)lockDuration.TotalSeconds
                },
                cancellationToken: cancellationToken));
    }

    public async Task ResetFailedVerificationsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.BankCustomers
            SET FailedVerificationCount = 0,
                LockedUntil = NULL
            WHERE CustomerId = @CustomerId
              AND (FailedVerificationCount <> 0 OR LockedUntil IS NOT NULL);
            """;

        await using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { CustomerId = customerId },
                cancellationToken: cancellationToken));
    }
}
