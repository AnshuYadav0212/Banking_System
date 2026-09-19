using System.ComponentModel.DataAnnotations;

namespace Banking_System.ApiService.DTOs;

/// <summary>Contact is the email address or phone number registered on the account.</summary>
public sealed record ForgotUsernameRequest(
    [Required, StringLength(256)] string Contact);

/// <summary>Identifier is the username, email address or phone number of the account.</summary>
public sealed record ForgotPasswordRequest(
    [Required, StringLength(256)] string Identifier);

/// <summary>DevResetLink is only ever filled in the Development environment.</summary>
public sealed record ForgotPasswordResponse(string? DevResetLink);

public sealed record ValidateResetLinkRequest(
    Guid ResetId,
    [Required, StringLength(100)] string Token);

public sealed record ResetPasswordRequest(
    Guid ResetId,
    [Required, StringLength(100)] string Token,
    [Required, MinLength(8), MaxLength(128)] string NewPassword);
