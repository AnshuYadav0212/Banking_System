using System.Security.Cryptography;
using System.Text;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;

namespace Banking_System.ApiService.Services;

public static class OtpPolicy
{
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ThrottleWindow = TimeSpan.FromHours(1);

    public const int MaxAttempts = 5;
    public const int MaxChallengesPerWindow = 5;
}

public enum OtpCheckStatus
{
    Valid,
    Invalid,
    Expired,
    TooManyAttempts
}

public sealed record OtpCheck(
    OtpCheckStatus Status,
    OtpChallenge? Challenge = null,
    int AttemptsLeft = 0);

/// <summary>Generates one-time tokens for emailed links and checks them against stored challenges.</summary>
public sealed class OtpService
{
    private readonly IOtpChallengeRepository _challenges;
    private readonly byte[] _key;

    public OtpService(
        IOtpChallengeRepository challenges,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _challenges = challenges;
        _key = Encoding.UTF8.GetBytes(
            configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT secret is not configured."));

        ExposeCodesForDevelopment = environment.IsDevelopment();
    }

    /// <summary>
    /// True only in the Development environment, where there is no SMS/email
    /// provider and the emailed link is returned to the caller so the flow can be tried.
    /// </summary>
    public bool ExposeCodesForDevelopment { get; }

    /// <summary>A 256-bit URL-safe random token, for emailed links.</summary>
    public static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Keyed hash (HMAC) so a leaked table cannot be brute-forced offline,
    // and bound to the challenge so a hash cannot be replayed on another one.
    public string Hash(Guid challengeId, string code) =>
        Convert.ToBase64String(
            HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));

    public async Task<OtpCheck> CheckAsync(
        Guid challengeId,
        Guid purposeId,
        string code,
        CancellationToken cancellationToken,
        bool countAttempt = true)
    {
        var challenge = await _challenges.GetAsync(challengeId, cancellationToken);

        if (challenge is null
            || challenge.PurposeId != purposeId
            || challenge.ConsumedAt is not null
            || challenge.ExpiresAt < DateTime.UtcNow)
        {
            return new OtpCheck(OtpCheckStatus.Expired);
        }

        // Counted atomically before comparing, so parallel guesses cannot
        // exceed the attempt limit. A read-only check (countAttempt: false)
        // only looks at the challenge, e.g. to decide whether a link page is
        // worth showing.
        var attempt = OtpPolicy.MaxAttempts;

        if (countAttempt)
        {
            var counted = await _challenges.RegisterAttemptAsync(challengeId, cancellationToken);

            if (counted is null)
            {
                return new OtpCheck(OtpCheckStatus.Expired);
            }

            if (counted > OtpPolicy.MaxAttempts)
            {
                return new OtpCheck(OtpCheckStatus.TooManyAttempts);
            }

            attempt = counted.Value;
        }
        else if (challenge.Attempts >= OtpPolicy.MaxAttempts)
        {
            return new OtpCheck(OtpCheckStatus.TooManyAttempts);
        }

        var expected = Encoding.UTF8.GetBytes(challenge.OtpHash);
        var actual = Encoding.UTF8.GetBytes(Hash(challengeId, code));

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return new OtpCheck(
                OtpCheckStatus.Invalid,
                AttemptsLeft: OtpPolicy.MaxAttempts - attempt);
        }

        return new OtpCheck(OtpCheckStatus.Valid, challenge);
    }
}
