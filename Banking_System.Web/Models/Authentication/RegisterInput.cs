using System.ComponentModel.DataAnnotations;

namespace Banking_System.Web.Models.Authentication;

/// <summary>Online-banking registration for an existing bank customer.</summary>
public sealed class RegisterInput : IValidatableObject
{
    public const string UsernamePattern = "^[A-Za-z0-9_]{3,30}$";

    [Required(ErrorMessage = "Enter your account number or CIF number.")]
    [RegularExpression(@"^[A-Za-z0-9\-]{6,20}$",
        ErrorMessage = "Enter 6-20 letters or digits.")]
    public string AccountOrCif { get; set; } = string.Empty;

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"^\+?[0-9\s\-()]{10,20}$",
        ErrorMessage = "Enter a valid phone number (10-15 digits, optional +).")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a username.")]
    [RegularExpression(UsernamePattern,
        ErrorMessage = "Use 3-30 letters, numbers or underscores.")]
    public string Username { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    [StringLength(30)]
    public string? NationalId { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(8, ErrorMessage = "Use at least 8 characters.")]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your password.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateOfBirth is null && string.IsNullOrWhiteSpace(NationalId))
        {
            yield return new ValidationResult(
                "Enter your date of birth or your national ID.",
                [nameof(DateOfBirth)]);
        }
    }

    public RegisterRequest ToRequest() => new(
        AccountOrCif.Trim(),
        FirstName.Trim(),
        LastName.Trim(),
        PhoneNumber.Trim(),
        Email.Trim(),
        Username.Trim(),
        DateOfBirth,
        string.IsNullOrWhiteSpace(NationalId) ? null : NationalId.Trim(),
        Password);
}

public sealed class ForgotUsernameInput
{
    [Required(ErrorMessage = "Enter your email address or phone number.")]
    [StringLength(256)]
    public string Contact { get; set; } = string.Empty;
}

public sealed class ForgotPasswordInput
{
    [Required(ErrorMessage = "Enter your username, email address or phone number.")]
    [StringLength(256)]
    public string Identifier { get; set; } = string.Empty;
}

public sealed class ResetPasswordInput
{
    // Both come from the emailed link and are carried through the form.
    public Guid ResetId { get; set; }

    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password.")]
    [MinLength(8, ErrorMessage = "Use at least 8 characters.")]
    [MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
