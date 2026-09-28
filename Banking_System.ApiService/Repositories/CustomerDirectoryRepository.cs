using Banking_System.ApiService.Data;
using Dapper;

namespace Banking_System.ApiService.Repositories;

/// <summary>Unmasked directory row; the service masks it before anything leaves the API.</summary>
public sealed class CustomerDirectoryEntry
{
    public string CifNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool HasOnlineBanking { get; set; }

    public string Status { get; set; } = string.Empty;

    public int AccountCount { get; set; }

    public decimal TotalBalance { get; set; }

    public int TotalCount { get; set; }
}

public interface ICustomerDirectoryRepository
{
    /// <param name="status">"Active", "Inactive" or "Locked"; null for all.</param>
    /// <param name="onlineBanking">true = registered only, false = not registered only, null = all.</param>
    Task<IReadOnlyList<CustomerDirectoryEntry>> SearchAsync(
        string? search, string? status, bool? onlineBanking, int page, int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed class CustomerDirectoryRepository : ICustomerDirectoryRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public CustomerDirectoryRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<CustomerDirectoryEntry>> SearchAsync(
        string? search, string? status, bool? onlineBanking, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<CustomerDirectoryEntry>(new CommandDefinition(
            """
            SELECT
                c.CifNumber,
                c.FirstName,
                c.LastName,
                c.PhoneNumber,
                u.Email,
                CAST(CASE WHEN u.UserId IS NULL THEN 0 ELSE 1 END AS bit) AS HasOnlineBanking,
                x.StatusName AS Status,
                ISNULL(a.AccountCount, 0) AS AccountCount,
                ISNULL(a.TotalBalance, 0) AS TotalBalance,
                COUNT(*) OVER () AS TotalCount
            FROM dbo.BankCustomers c
            JOIN dbo.Statuses s ON s.StatusId = c.StatusId
            LEFT JOIN dbo.Users u ON u.BankCustomerId = c.CustomerId
            CROSS APPLY (SELECT CASE WHEN c.LockedUntil > SYSUTCDATETIME() THEN N'Locked' ELSE s.Name END) x (StatusName)
            OUTER APPLY (
                SELECT COUNT(*) AS AccountCount, SUM(b.AvailableBalance) AS TotalBalance
                FROM dbo.BankAccounts b
                WHERE b.CustomerId = c.CustomerId) a
            WHERE (@Like IS NULL
                   OR c.FirstName + N' ' + c.LastName LIKE @Like ESCAPE N'\'
                   OR c.CifNumber LIKE @Like ESCAPE N'\'
                   OR c.PhoneNumber LIKE @Like ESCAPE N'\'
                   OR u.Email LIKE @Like ESCAPE N'\')
              AND (@Status IS NULL OR x.StatusName = @Status)
              AND (@OnlineBanking IS NULL
                   OR (@OnlineBanking = 1 AND u.UserId IS NOT NULL)
                   OR (@OnlineBanking = 0 AND u.UserId IS NULL))
            ORDER BY c.LastName, c.FirstName, c.CifNumber
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """,
            new
            {
                Like = ToLikePattern(search),
                Status = status,
                OnlineBanking = onlineBanking,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            },
            cancellationToken: cancellationToken));

        return rows.ToList();
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
}
