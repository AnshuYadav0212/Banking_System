using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;
using Banking_System.ApiService.Validation;
using Microsoft.AspNetCore.Identity;

namespace Banking_System.ApiService.Services;

public enum ResetPasswordStatus
{
    Success,
    InvalidLink,
    TooManyAttempts
}

public sealed record ResetPasswordResult(ResetPasswordStatus Status);

/// <summary>
/// "Forgot username" and "forgot password". Neither ever reveals whether an
/// account exists, and passwords are never sent (they are stored only as
/// hashes): a forgotten password is reset through a single-use link emailed to
/// the address on the account.
/// </summary>
public sealed class AccountRecoveryService
{
    private readonly IUserRepository _users;
    private readonly IOtpChallengeRepository _challenges;
    private readonly OtpService _otp;
    private readonly INotificationSender _notifications;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<AccountRecoveryService> _logger;
    private readonly string _webBaseUrl;

    public AccountRecoveryService(
        IUserRepository users,
        IOtpChallengeRepository challenges,
        OtpService otp,
        INotificationSender notifications,
        IPasswordHasher<User> passwordHasher,
        ILogger<AccountRecoveryService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _webBaseUrl = (configuration["Web:BaseUrl"] ?? string.Empty).TrimEnd('/');
        _users = users;
        _challenges = challenges;
        _otp = otp;
        _notifications = notifications;
        _passwordHasher = passwordHasher;
    }

    /// <summary>Sends the username(s) to the email/phone entered, if they belong to an account.</summary>
    public async Task RecoverUsernameAsync(string contact, CancellationToken cancellationToken)
    {
        contact = contact.Trim();

        string? email = null;
        string? phone = null;
        IReadOnlyList<User> users;

        if (contact.Contains('@'))
        {
            email = contact.ToLowerInvariant();
            var user = await _users.GetByEmailAsync(email, cancellationToken);
            users = user is { IsActive: true } ? [user] : [];
        }
        else if (ContactRules.IsValidPhone(contact))
        {
            phone = ContactRules.NormalizePhone(contact);
            users = await _users.GetByPhoneAsync(phone, cancellationToken);
        }
        else
        {
            return;
        }

        // The throttle log is written whether or not an account matched, so the
        // limit itself reveals nothing.
        var recent = await _challenges.CountRecentAsync(
            OtpPurposes.UsernameRecovery,
            bankCustomerId: null,
            userId: null,
            email,
            phone,
            DateTime.UtcNow - OtpPolicy.ThrottleWindow,
            cancellationToken);

        if (recent >= OtpPolicy.MaxChallengesPerWindow)
        {
            return;
        }

        var now = DateTime.UtcNow;

        await _challenges.CreateAsync(
            new OtpChallenge
            {
                ChallengeId = Guid.NewGuid(),
                Purpose = OtpPurposes.UsernameRecovery,
                Email = email,
                PhoneNumber = phone,
                OtpHash = "-",
                CreatedAt = now,
                LastSentAt = now,
                ExpiresAt = now,
                ConsumedAt = now
            },
            cancellationToken);

        var usernames = users
            .Where(u => !string.IsNullOrEmpty(u.Username))
            .Select(u => u.Username!)
            .ToList();

        if (usernames.Count > 0)
        {
            await _notifications.SendUsernamesAsync(
                new UsernameMessage(email, phone, usernames),
                cancellationToken);
        }
    }

    /// <summary>
    /// Emails a reset link when the identifier belongs to an account. The
    /// result never says whether one did; in Development it also carries the
    /// link, because no mail server is connected.
    /// </summary>
    public async Task<ForgotPasswordResponse> StartPasswordResetAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(identifier.Trim(), cancellationToken);

        if (user is null)
        {
            return new ForgotPasswordResponse(null);
        }

        var recent = await _challenges.CountRecentAsync(
            OtpPurposes.PasswordReset,
            bankCustomerId: null,
            user.UserId,
            email: null,
            phone: null,
            DateTime.UtcNow - OtpPolicy.ThrottleWindow,
            cancellationToken);

        if (recent >= OtpPolicy.MaxChallengesPerWindow)
        {
            return new ForgotPasswordResponse(null);
        }

        var now = DateTime.UtcNow;
        var token = OtpService.GenerateToken();
        var challengeId = Guid.NewGuid();

        await _challenges.CreateAsync(
            new OtpChallenge
            {
                ChallengeId = challengeId,
                Purpose = OtpPurposes.PasswordReset,
                UserId = user.UserId,
                Email = user.Email,
                OtpHash = _otp.Hash(challengeId, token),
                CreatedAt = now,
                LastSentAt = now,
                ExpiresAt = now + OtpPolicy.ResetLinkLifetime
            },
            cancellationToken);

        var link = $"{_webBaseUrl}/reset-password?id={challengeId:N}&token={token}";

        try
        {
            await _notifications.SendPasswordResetLinkAsync(
                new EmailLinkMessage(user.Email, user.FirstName, link, OtpPolicy.ResetLinkLifetime),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // A delivery failure must not change the response, or it would
            // reveal that the account exists.
            _logger.LogError(ex, "Could not send a password reset email.");
        }

        return new ForgotPasswordResponse(_otp.ExposeCodesForDevelopment ? link : null);
    }

    /// <summary>Read-only check used to decide whether the reset page should show its form.</summary>
    public async Task<bool> IsResetLinkValidAsync(
        Guid resetId,
        string token,
        CancellationToken cancellationToken)
    {
        var check = await _otp.CheckAsync(
            resetId,
            OtpPurposes.PasswordReset,
            token,
            cancellationToken,
            countAttempt: false);

        return check.Status == OtpCheckStatus.Valid;
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var check = await _otp.CheckAsync(
            request.ResetId,
            OtpPurposes.PasswordReset,
            request.Token,
            cancellationToken);

        if (check.Status == OtpCheckStatus.TooManyAttempts)
        {
            return new ResetPasswordResult(ResetPasswordStatus.TooManyAttempts);
        }

        if (check.Status != OtpCheckStatus.Valid)
        {
            return new ResetPasswordResult(ResetPasswordStatus.InvalidLink);
        }

        var challenge = check.Challenge!;

        // Spend the link first so it can never be used twice.
        if (!await _challenges.ConsumeAsync(challenge.ChallengeId, cancellationToken))
        {
            return new ResetPasswordResult(ResetPasswordStatus.InvalidLink);
        }

        var user = await _users.GetByIdAsync(challenge.UserId!.Value, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return new ResetPasswordResult(ResetPasswordStatus.InvalidLink);
        }

        await _users.UpdatePasswordHashAsync(
            user.UserId,
            _passwordHasher.HashPassword(user, request.NewPassword),
            cancellationToken);

        return new ResetPasswordResult(ResetPasswordStatus.Success);
    }

    private async Task<User?> FindUserAsync(string identifier, CancellationToken cancellationToken)
    {
        User? user;

        if (identifier.Contains('@'))
        {
            user = await _users.GetByEmailAsync(identifier.ToLowerInvariant(), cancellationToken);
        }
        else if (ContactRules.IsValidPhone(identifier))
        {
            // A phone number can be shared, so it only identifies an account
            // when exactly one login uses it.
            var matches = await _users.GetByPhoneAsync(ContactRules.NormalizePhone(identifier), cancellationToken);
            user = matches.Count == 1 ? matches[0] : null;
        }
        else
        {
            user = await _users.GetByUsernameAsync(UsernameRules.Normalize(identifier), cancellationToken);
        }

        return user is { IsActive: true } ? user : null;
    }
}
