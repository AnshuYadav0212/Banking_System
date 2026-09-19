using Banking_System.ApiService.Data;
using Banking_System.ApiService.Models;
using Dapper;

namespace Banking_System.ApiService.Repositories;

public interface IOtpChallengeRepository
{
    Task CreateAsync(OtpChallenge challenge, CancellationToken cancellationToken = default);

    Task<OtpChallenge?> GetAsync(Guid challengeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically counts one verification attempt and returns the new total,
    /// or null when the challenge is unknown or already used.
    /// </summary>
    Task<int?> RegisterAttemptAsync(Guid challengeId, CancellationToken cancellationToken = default);

    /// <summary>Marks the challenge used. Returns false if someone else already did.</summary>
    Task<bool> ConsumeAsync(Guid challengeId, CancellationToken cancellationToken = default);

    Task<int> CountRecentAsync(
        string purpose,
        Guid? bankCustomerId,
        Guid? userId,
        string? email,
        string? phone,
        DateTime since,
        CancellationToken cancellationToken = default);
}

public sealed class OtpChallengeRepository : IOtpChallengeRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public OtpChallengeRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(
        OtpChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO dbo.OtpChallenges
            (
                ChallengeId, Purpose, BankCustomerId, UserId, Username, Email,
                PhoneNumber, PasswordHash, OtpHash, Attempts, ResendCount,
                CreatedAt, LastSentAt, ExpiresAt, ConsumedAt
            )
            VALUES
            (
                @ChallengeId, @Purpose, @BankCustomerId, @UserId, @Username, @Email,
                @PhoneNumber, @PasswordHash, @OtpHash, @Attempts, @ResendCount,
                @CreatedAt, @LastSentAt, @ExpiresAt, @ConsumedAt
            );
            """;

        await using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(
            new CommandDefinition(sql, challenge, cancellationToken: cancellationToken));
    }

    public async Task<OtpChallenge?> GetAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT ChallengeId, Purpose, BankCustomerId, UserId, Username, Email,
                   PhoneNumber, PasswordHash, OtpHash, Attempts, ResendCount,
                   CreatedAt, LastSentAt, ExpiresAt, ConsumedAt
            FROM dbo.OtpChallenges
            WHERE ChallengeId = @ChallengeId;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<OtpChallenge>(
            new CommandDefinition(
                sql,
                new { ChallengeId = challengeId },
                cancellationToken: cancellationToken));
    }

    public async Task<int?> RegisterAttemptAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.OtpChallenges
            SET Attempts = Attempts + 1
            OUTPUT inserted.Attempts
            WHERE ChallengeId = @ChallengeId
              AND ConsumedAt IS NULL;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(
                sql,
                new { ChallengeId = challengeId },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> ConsumeAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.OtpChallenges
            SET ConsumedAt = SYSUTCDATETIME()
            WHERE ChallengeId = @ChallengeId
              AND ConsumedAt IS NULL;
            """;

        await using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { ChallengeId = challengeId },
                cancellationToken: cancellationToken));

        return rows == 1;
    }

    public async Task<int> CountRecentAsync(
        string purpose,
        Guid? bankCustomerId,
        Guid? userId,
        string? email,
        string? phone,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.OtpChallenges
            WHERE Purpose = @Purpose
              AND CreatedAt >= @Since
              AND (   (@BankCustomerId IS NOT NULL AND BankCustomerId = @BankCustomerId)
                   OR (@UserId IS NOT NULL AND UserId = @UserId)
                   OR (@Email IS NOT NULL AND Email = @Email)
                   OR (@Phone IS NOT NULL AND PhoneNumber = @Phone));
            """;

        await using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                sql,
                new
                {
                    Purpose = purpose,
                    Since = since,
                    BankCustomerId = bankCustomerId,
                    UserId = userId,
                    Email = email,
                    Phone = phone
                },
                cancellationToken: cancellationToken));
    }
}
