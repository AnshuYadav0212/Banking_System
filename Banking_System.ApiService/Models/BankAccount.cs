namespace Banking_System.ApiService.Models;

/// <summary>A bank account. The key is <see cref="AccountId"/>; the number is what customers see.</summary>
public sealed class BankAccount
{
    public Guid AccountId { get; set; }

    public string AccountNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public decimal AvailableBalance { get; set; }

    /// <summary>Key into the Statuses table (see <see cref="StatusIds"/>).</summary>
    public Guid StatusId { get; set; }

    public bool IsActive => StatusId == StatusIds.Active;
}

/// <summary>Everything needed to decide whether an account can receive an online transfer.</summary>
public sealed class RecipientLookup
{
    public Guid AccountId { get; set; }

    public string AccountNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Guid AccountStatusId { get; set; }

    public Guid CustomerStatusId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>True when the account's customer has an online-banking login that is active.</summary>
    public bool HasActiveLogin { get; set; }

    public bool AccountIsActive => AccountStatusId == StatusIds.Active;

    public bool CustomerIsActive => CustomerStatusId == StatusIds.Active;
}

/// <summary>A stored transfer, with the account numbers and owners read through joins.</summary>
public sealed class TransactionRecord
{
    public Guid TransactionId { get; set; }

    public Guid RequestId { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal Amount { get; set; }

    public string? Comment { get; set; }

    public Guid InitiatedByUserId { get; set; }

    public Guid FromAccountId { get; set; }

    public Guid ToAccountId { get; set; }

    public string FromAccountNumber { get; set; } = string.Empty;

    public string ToAccountNumber { get; set; } = string.Empty;

    public Guid FromCustomerId { get; set; }

    public Guid ToCustomerId { get; set; }

    public string FromFirstName { get; set; } = string.Empty;

    public string FromLastName { get; set; } = string.Empty;

    public string ToFirstName { get; set; } = string.Empty;

    public string ToLastName { get; set; } = string.Empty;
}
