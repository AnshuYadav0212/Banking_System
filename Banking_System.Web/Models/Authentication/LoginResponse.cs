namespace Banking_System.Web.Models.Authentication;

public sealed record LoginResponse(
    string AccessToken,
    string UserId,
    string Email,
    string Role);
