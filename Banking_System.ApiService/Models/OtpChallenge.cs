namespace Banking_System.ApiService.Models;

/// <summary>
/// A short-lived one-time token. PasswordReset rows identify the login by
/// <see cref="UserId"/> only; UsernameRecovery rows are a throttling log keyed
/// by the email or phone that was asked about.
/// </summary>
public sealed class OtpChallenge
{
    public Guid ChallengeId { get; set; }

    /// <summary>Key into the OtpPurposes table (see <see cref="OtpPurposeIds"/>).</summary>
    public Guid PurposeId { get; set; }

    public Guid? UserId { get; set; }

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string OtpHash { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }
}
