using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Validation;
using Dapper;

namespace Banking_System.ApiService.Repositories;

public sealed class UserRepository : IUserRepository
{
    private const string SelectColumns = """
        SELECT
            UserId,
            Username,
            FirstName,
            LastName,
            Email,
            PhoneNumber,
            DateOfBirth,
            BankCustomerId,
            Gender,
            Address,
            City,
            State,
            PostalCode,
            PasswordHash,
            Role,
            IsActive,
            CreatedAt,
            UpdatedAt
        FROM dbo.Users
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
                SelectColumns + " WHERE Email = @Email;",
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
                SelectColumns + " WHERE Username = @Username;",
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
                SelectColumns + " WHERE UserId = @UserId;",
                new { UserId = userId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<User>> GetByPhoneAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();

        // PhoneLast10 is an indexed computed column; the full comparison then
        // confirms the match, so different country codes cannot collide.
        var candidates = await connection.QueryAsync<User>(
            new CommandDefinition(
                SelectColumns + " WHERE PhoneLast10 = @Last10 AND IsActive = 1;",
                new { Last10 = ContactRules.Last10Digits(phone) },
                cancellationToken: cancellationToken));

        return candidates
            .Where(u => ContactRules.PhonesMatch(u.PhoneNumber, phone))
            .ToList();
    }

    public async Task<bool> PhoneInUseAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT PhoneNumber
            FROM dbo.Users
            WHERE PhoneLast10 = @Last10;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        // The indexed last-10-digits column finds the candidates; the full
        // comparison then confirms it is really the same number.
        var candidates = await connection.QueryAsync<string>(
            new CommandDefinition(
                sql,
                new { Last10 = ContactRules.Last10Digits(phone) },
                cancellationToken: cancellationToken));

        return candidates.Any(existing => ContactRules.PhonesMatch(existing, phone));
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
                Username,
                FirstName,
                LastName,
                Email,
                PhoneNumber,
                DateOfBirth,
                BankCustomerId,
                Gender,
                Address,
                City,
                State,
                PostalCode,
                PasswordHash,
                Role,
                IsActive,
                CreatedAt
            )
            VALUES
            (
                @UserId,
                @Username,
                @FirstName,
                @LastName,
                @Email,
                @PhoneNumber,
                @DateOfBirth,
                @BankCustomerId,
                @Gender,
                @Address,
                @City,
                @State,
                @PostalCode,
                @PasswordHash,
                @Role,
                @IsActive,
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
