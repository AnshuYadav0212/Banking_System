using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Dapper;

namespace Banking_System.ApiService.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public UserRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                UserId,
                Email,
                PasswordHash,
                Role,
                IsActive,
                CreatedAt,
                UpdatedAt
            FROM dbo.Users
            WHERE Email = @Email;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<User>(
            new CommandDefinition(
                sql,
                new { Email = email },
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
                Email,
                PasswordHash,
                Role,
                IsActive,
                CreatedAt
            )
            VALUES
            (
                @UserId,
                @Email,
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
