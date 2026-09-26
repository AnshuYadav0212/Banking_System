using System.ComponentModel.DataAnnotations;
using Banking_System.ApiService.Validation;

namespace Banking_System.ApiService.DTOs;

/// <summary>
/// Self-service registration for staff. Anyone whose email is on the bank's staff
/// domain (<see cref="StaffEmailRules.EmployeeDomain"/>) can create an Employee login
/// this way; there is no bank-record check like there is for customers.
/// </summary>
public sealed record StaffRegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, RegularExpression(UsernameRules.Pattern)] string Username,
    [Required, MinLength(8), MaxLength(128)] string Password) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!StaffEmailRules.IsEmployeeEmail(Email))
        {
            yield return new ValidationResult(
                $"Staff registration needs an email address ending in @{StaffEmailRules.EmployeeDomain}.",
                [nameof(Email)]);
        }
    }
}
