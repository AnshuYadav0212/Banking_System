namespace Banking_System.ApiService.Services;

public sealed record UsernameMessage(
    string? Email,
    string? Phone,
    IReadOnlyList<string> Usernames);

public sealed record EmailLinkMessage(
    string Email,
    string? FirstName,
    string Link,
    TimeSpan ValidFor);

/// <summary>
/// Delivers messages to customers. Email is sent by <see cref="SmtpNotificationSender"/>
/// once a mail server is configured; an SMS gateway can be added by implementing
/// this interface and registering it in Program.cs.
/// </summary>
public interface INotificationSender
{
    Task SendPasswordResetLinkAsync(EmailLinkMessage message, CancellationToken cancellationToken = default);

    Task SendUsernamesAsync(UsernameMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Placeholder sender used when no mail server is configured: nothing is
/// actually delivered. In Development the content is written to the log so the
/// flows can be tried; elsewhere only an error is written and secrets are never
/// logged.
/// </summary>
public sealed class LoggingNotificationSender : INotificationSender
{
    private readonly ILogger<LoggingNotificationSender> _logger;
    private readonly IHostEnvironment _environment;

    public LoggingNotificationSender(
        ILogger<LoggingNotificationSender> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public Task SendPasswordResetLinkAsync(EmailLinkMessage message, CancellationToken cancellationToken = default) =>
        LogLink("password reset", message);

    public Task SendUsernamesAsync(UsernameMessage message, CancellationToken cancellationToken = default)
    {
        if (_environment.IsDevelopment())
        {
            _logger.LogWarning(
                "DEV ONLY - username(s) {Usernames} for email {Email}, phone {Phone}",
                string.Join(", ", message.Usernames), message.Email, message.Phone);
        }
        else
        {
            _logger.LogError("No notification provider is configured: a username reminder could not be delivered.");
        }

        return Task.CompletedTask;
    }

    private Task LogLink(string kind, EmailLinkMessage message)
    {
        if (_environment.IsDevelopment())
        {
            _logger.LogWarning(
                "DEV ONLY - {Kind} link for {Email} (valid {Minutes} min): {Link}",
                kind, message.Email, message.ValidFor.TotalMinutes, message.Link);
        }
        else
        {
            _logger.LogError("No mail server is configured: a {Kind} email could not be delivered.", kind);
        }

        return Task.CompletedTask;
    }
}
