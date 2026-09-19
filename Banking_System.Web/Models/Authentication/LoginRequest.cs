namespace Banking_System.Web.Models.Authentication;

/// <summary>Identifier is either the user's username or email address.</summary>
public sealed record LoginRequest(
    string Identifier,
    string Password);
