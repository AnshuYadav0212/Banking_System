using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Validation;
using Dapper;

namespace Banking_System.ApiService.Repositories;

public sealed class UserRepository : IUserRepository
{
    // The role name comes from the Roles lookup; name and phone come from the bank
    // customer record. None of them is copied onto Users.
    private const string SelectColumns = """
        SELECT
            u.UserId,
            u.BankCustomerId,
            u.Username,
            u.Email,
            u.PasswordHash,
            u.RoleId,
            r.Name AS Role,
            u.StatusId,
            u.CreatedAt,
            u.UpdatedAt,
            c.FirstName,
            c.LastName,
            c.PhoneNumber
        FROM dbo.Users u
        JOIN dbo.Roles r ON r.RoleId = u.RoleId
        LEFT JOIN dbo.BankCustomers c ON c.CustomerId = u.BankCustomerId
        """;

    private readonly ISqlConnectionFactory _connectionFactory;

    public UserRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<User>(
            new CommandDefinition(
                SelectColumns + " WHERE u.Email = @Email;",
                new { Email = email },
                cancellationToken: cancellationToken));
    }

    public async Task<User?> GetByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<User>(
            new CommandDefinition(
                SelectColumns + " WHERE u.Username = @Username;",
                new { Username = username },
                cancellationToken: cancellationToken));
    }

    public async Task<User?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<User>(
            new CommandDefinition(
                SelectColumns + " WHERE u.UserId = @UserId;",
                new { UserId = userId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<User>> GetByPhoneAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        if (digits.Length < 10)
        {
            return [];
        }

        await using var connection = _connectionFactory.CreateConnection();

        // The number lives on the bank customer. People type it with or without
        // the country code, so match exactly or as a suffix (digits only, so
        // nothing to escape). Used only by account recovery, which is rate limited.
        var candidates = await connection.QueryAsync<User>(
            new CommandDefinition(
                SelectColumns + " WHERE u.StatusId = @Active AND (c.PhoneNumber = @Exact OR c.PhoneNumber LIKE @Suffix);",
                new { Active = StatusIds.Active, Exact = ContactRules.NormalizePhone(phone), Suffix = "%" + digits },
                cancellationToken: cancellationToken));

        return candidates
            .Where(u => ContactRules.PhonesMatch(u.PhoneNumber, phone))
            .ToList();
    }

    // True if a different customer who already has a login is recorded with this
    // phone number. The number is read from the bank customer, so there is no
    // copy on Users to keep in step.
    public async Task<bool> PhoneInUseAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.Users u
                JOIN dbo.BankCustomers c ON c.CustomerId = u.BankCustomerId
                WHERE c.PhoneNumber = @Phone)
            THEN 1 ELSE 0 END;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { Phone = phone },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> BankCustomerHasLoginAsync(
        Guid bankCustomerId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Users WHERE BankCustomerId = @BankCustomerId)
            THEN 1 ELSE 0 END;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { BankCustomerId = bankCustomerId },
                cancellationToken: cancellationToken));
    }

    public async Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Users
            SET PasswordHash = @PasswordHash,
                UpdatedAt = SYSUTCDATETIME()
            WHERE UserId = @UserId;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { UserId = userId, PasswordHash = passwordHash },
                cancellationToken: cancellationToken));
    }

    // Seeks on the unique filtered index UX_Users_Username, so it stays cheap
    // however many users exist.
    public async Task<bool> UsernameExistsAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Users WHERE Username = @Username)
            THEN 1 ELSE 0 END;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { Username = username },
                cancellationToken: cancellationToken));
    }

    public async Task CreateAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO dbo.Users
            (
                UserId,
                BankCustomerId,
                Username,
                Email,
                PasswordHash,
                RoleId,
                StatusId,
                CreatedAt
            )
            VALUES
            (
                @UserId,
                @BankCustomerId,
                @Username,
                @Email,
                @PasswordHash,
                @RoleId,
                @StatusId,
                @CreatedAt
            );
            """;

        await using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                user,
                cancellationToken: cancellationToken));
    }
}
