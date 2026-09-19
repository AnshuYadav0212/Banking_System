namespace Banking_System.ApiService.Models;

public static class OtpPurposes
{
    public const string PasswordReset = "PasswordReset";
    public const string UsernameRecovery = "UsernameRecovery";
}

public sealed class OtpChallenge
{
    public Guid ChallengeId { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public Guid? BankCustomerId { get; set; }

    public Guid? UserId { get; set; }

    public string? Username { get; set; }

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string? PasswordHash { get; set; }

    public string OtpHash { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public int ResendCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastSentAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }
}
