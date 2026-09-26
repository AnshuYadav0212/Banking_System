namespace Banking_System.Web.Models.Authentication;

/// <summary>Sent to the API to register; the API checks it against the bank's records.</summary>
public sealed record RegisterRequest(
    string AccountOrCif,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string Email,
    string Username,
    DateOnly? DateOfBirth,
    string? NationalId,
    string Password);

/// <summary>DevResetLink is only present when the API runs in Development (no mail server).</summary>
public sealed record ForgotPasswordResponse(string? DevResetLink);

/// <summary>Self-service staff registration; the API only accepts this for the staff email domain.</summary>
public sealed record StaffRegisterRequest(string Email, string Username, string Password);
