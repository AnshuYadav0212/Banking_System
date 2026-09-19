using System.Net;
using System.Net.Mail;

namespace Banking_System.ApiService.Services;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string? UserName { get; set; }

    /// <summary>Keep this out of appsettings.json: use user-secrets or an environment variable.</summary>
    public string? Password { get; set; }

    public string From { get; set; } = string.Empty;

    public string FromName { get; set; } = "SecureBank";
}

/// <summary>
/// Sends every notification by email over SMTP. There is no SMS gateway, so a
/// username reminder addressed only to a phone number is logged as unsent.
/// </summary>
public sealed class SmtpNotificationSender : INotificationSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpNotificationSender> _logger;

    public SmtpNotificationSender(
        IConfiguration configuration,
        ILogger<SmtpNotificationSender> logger)
    {
        _options = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>()
            ?? throw new InvalidOperationException("Smtp configuration is missing.");
        _logger = logger;
    }

    public Task SendPasswordResetLinkAsync(EmailLinkMessage message, CancellationToken cancellationToken = default) =>
        SendEmailAsync(
            message.Email,
            "Reset your SecureBank password",
            $"""
            <p>{Greeting(message.FirstName)}</p>
            <p>We received a request to reset your SecureBank password. Use the button below to choose a new one.
            The link works once and expires in {Describe(message.ValidFor)}.</p>
            {Button(message.Link, "Reset password")}
            <p>If you did not ask for this, ignore this email: your password has not been changed.</p>
            """,
            cancellationToken);

    public async Task SendUsernamesAsync(UsernameMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Email is null)
        {
            _logger.LogWarning("No SMS gateway is configured: a username reminder was not sent by SMS.");
            return;
        }

        var list = string.Join("", message.Usernames.Select(u => $"<li><strong>{WebUtility.HtmlEncode(u)}</strong></li>"));

        await SendEmailAsync(
            message.Email,
            "Your SecureBank username",
            $"""
            <p>You asked for your SecureBank online-banking username. It is:</p>
            <ul>{list}</ul>
            <p>If you did not ask for this, you can ignore this email.</p>
            """,
            cancellationToken);
    }

    private static string Greeting(string? firstName) =>
        string.IsNullOrWhiteSpace(firstName) ? "Hello," : $"Hello {WebUtility.HtmlEncode(firstName)},";

    private static string Describe(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{span.TotalHours:0} hours" : $"{span.TotalMinutes:0} minutes";

    private static string Button(string link, string label)
    {
        var encoded = WebUtility.HtmlEncode(link);

        return $"""
            <p><a href="{encoded}" style="background:#1b6ec2;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">{label}</a></p>
            <p>If the button does not work, copy this address into your browser:<br>{encoded}</p>
            """;
    }

    private async Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = string.IsNullOrEmpty(_options.UserName)
                ? null
                : new NetworkCredential(_options.UserName, _options.Password)
        };

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.From, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        mail.To.Add(to);

        await client.SendMailAsync(mail, cancellationToken);
    }
}
