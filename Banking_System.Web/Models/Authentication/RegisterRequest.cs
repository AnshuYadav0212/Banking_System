namespace Banking_System.Web.Models.Authentication;

public sealed record RegisterRequest(
    string Email,
    string Password);
