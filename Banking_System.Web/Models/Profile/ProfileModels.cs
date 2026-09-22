using System.ComponentModel.DataAnnotations;

namespace Banking_System.Web.Models.Profile;

public sealed record MyProfile(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string PhoneNumber,
    bool IsActive);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>The change-password form.</summary>
public sealed class ChangePasswordInput : IValidatableObject
{
    [Required(ErrorMessage = "Enter your current password.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password.")]
    [StringLength(200, MinimumLength = 8, ErrorMessage = "The new password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your new password.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NewPassword.Length > 0 && NewPassword != ConfirmPassword)
        {
            yield return new ValidationResult("The passwords do not match.", [nameof(ConfirmPassword)]);
        }
    }
}
