using System.ComponentModel.DataAnnotations;

namespace Banking_System.ApiService.DTOs;

/// <summary>The signed-in customer's personal and contact details, read from their bank record and login.</summary>
public sealed record MyProfileResponse(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string PhoneNumber,
    bool IsActive);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8, ErrorMessage = "The new password must be at least 8 characters.")] string NewPassword);
