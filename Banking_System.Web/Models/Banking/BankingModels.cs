using System.ComponentModel.DataAnnotations;

namespace Banking_System.Web.Models.Banking;

public sealed record AccountSummary(
    Guid AccountId,
    string AccountNumber,
    decimal AvailableBalance,
    bool IsActive);

/// <summary>One line of the customer's transfer history, seen from their side.</summary>
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

public sealed record TransferRequest(
    Guid FromAccountId,
    string ToAccountNumber,
    decimal Amount,
    string? Comment,
    Guid RequestId);

public sealed record TransferResponse(
    Guid TransactionId,
    DateTime CreatedAt,
    decimal Amount,
    string FromAccountNumber,
    string ToAccountNumber,
    string RecipientName,
    decimal NewBalance);

/// <summary>The send-money form.</summary>
public sealed class TransferInput : IValidatableObject
{
    public const decimal MaxAmount = 999_999_999_999.99m;

    /// <summary>
    /// Made when the form is first shown and carried through every submit. The
    /// server refuses to move the money twice for the same one, so a double click
    /// or a browser refresh cannot send it again.
    /// </summary>
    public Guid RequestId { get; set; } = Guid.NewGuid();

    public Guid FromAccountId { get; set; }

    [Required(ErrorMessage = "Enter the account number to send to.")]
    [RegularExpression(@"^[0-9]{6,20}$", ErrorMessage = "An account number is 6 to 20 digits.")]
    public string ToAccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the amount to send.")]
    public decimal? Amount { get; set; }

    [StringLength(200, ErrorMessage = "A note can be at most 200 characters.")]
    public string? Comment { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FromAccountId == Guid.Empty)
        {
            yield return new ValidationResult("Choose the account to send from.", [nameof(FromAccountId)]);
        }

        if (Amount is { } amount)
        {
            if (amount <= 0 || amount > MaxAmount)
            {
                yield return new ValidationResult("The amount must be greater than zero.", [nameof(Amount)]);
            }
            else if (decimal.Round(amount, 2) != amount)
            {
                yield return new ValidationResult("Use at most two decimal places.", [nameof(Amount)]);
            }
        }
    }

    public TransferRequest ToRequest() => new(
        FromAccountId,
        ToAccountNumber.Trim(),
        Amount!.Value,
        string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim(),
        RequestId);
}
