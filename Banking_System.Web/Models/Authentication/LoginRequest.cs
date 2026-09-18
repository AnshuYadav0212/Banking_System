namespace Banking_System.Web.Models.Authentication;

public sealed record LoginRequest(
    string Email,
    string Password);
