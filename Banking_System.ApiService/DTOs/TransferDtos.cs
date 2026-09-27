using System.ComponentModel.DataAnnotations;

namespace Banking_System.ApiService.DTOs;

/// <summary>Send money from one of the caller's own accounts to another account.</summary>
public sealed record TransferRequest(
    Guid FromAccountId,
    [Required, RegularExpression(@"^[0-9]{6,20}$")] string ToAccountNumber,
    decimal Amount,
    [StringLength(200)] string? Comment,
    Guid RequestId) : IValidatableObject
{
    public const decimal MaxAmount = 999_999_999_999.99m;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0 || Amount > MaxAmount)
        {
            yield return new ValidationResult("Amount must be greater than zero.", [nameof(Amount)]);
        }
        else if (decimal.Round(Amount, 2) != Amount)
        {
            yield return new ValidationResult("Amount can have at most two decimal places.", [nameof(Amount)]);
        }

        if (FromAccountId == Guid.Empty)
        {
            yield return new ValidationResult("Choose the account to send from.", [nameof(FromAccountId)]);
        }

        if (RequestId == Guid.Empty)
        {
            yield return new ValidationResult("A request id is required.", [nameof(RequestId)]);
        }
    }
}

public sealed record TransferResponse(
    Guid TransactionId,
    DateTime CreatedAt,
    decimal Amount,
    string FromAccountNumber,
    string ToAccountNumber,
    string RecipientName,
    decimal NewBalance);

public sealed record AccountSummary(
    Guid AccountId,
    string AccountNumber,
    decimal AvailableBalance,
    bool IsActive);

/// <summary>One line of a customer's transfer history, seen from their side.</summary>
public sealed record TransactionSummary(
    Guid TransactionId,
    DateTime CreatedAt,
    string Direction,
    string CounterpartyAccountNumber,
    string CounterpartyName,
    decimal Amount,
    string? Comment);

/// <summary>The account holder's name for an account number, shown before a transfer is sent.</summary>
public sealed record RecipientVerification(
    Guid AccountId,
    string AccountNumber,
    string RecipientName);
