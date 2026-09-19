namespace Banking_System.Web.Models.Authentication;

public sealed record UsernameAvailability(bool Available, string? Reason);
