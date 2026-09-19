using System.ComponentModel.DataAnnotations;
using Banking_System.ApiService.Validation;

namespace Banking_System.ApiService.DTOs;

/// <summary>
/// Online-banking registration for an existing bank customer.
/// The details are checked against the bank's own records.
/// </summary>
public sealed record RegisterRequest(
    [Required, RegularExpression(ContactRules.AccountOrCifPattern)] string AccountOrCif,
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName,
    [Required, RegularExpression(ContactRules.PhonePattern)] string PhoneNumber,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, RegularExpression(UsernameRules.Pattern)] string Username,
    DateOnly? DateOfBirth,
    [StringLength(30)] string? NationalId,
    [Required, MinLength(8), MaxLength(128)] string Password) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateOfBirth is null && string.IsNullOrWhiteSpace(NationalId))
        {
            yield return new ValidationResult(
                "Provide your date of birth or your national ID.",
                [nameof(DateOfBirth), nameof(NationalId)]);
        }
    }
}
