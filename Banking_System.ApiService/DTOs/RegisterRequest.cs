using System.ComponentModel.DataAnnotations;

namespace Banking_System.ApiService.DTOs;

public sealed record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password);
