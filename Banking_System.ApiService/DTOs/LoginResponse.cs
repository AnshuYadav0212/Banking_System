namespace Banking_System.ApiService.DTOs;

public sealed record LoginResponse(
    string AccessToken,
    string UserId,
    string Email,
    string Role);