using System.ComponentModel.DataAnnotations;

namespace Banking_System.ApiService.DTOs;

/// <summary>Identifier is either the user's username or email address.</summary>
public sealed record LoginRequest(
    [Required, StringLength(256)] string Identifier,
    [Required, StringLength(128)] string Password);
