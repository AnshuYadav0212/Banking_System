namespace Banking_System.ApiService.Models;

/// <summary>A customer as recorded by the bank (core banking), not an online-banking login.</summary>
public sealed class BankCustomer
{
    public Guid CustomerId { get; set; }

    public string CifNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    public string? NationalId { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Key into the Statuses table (see <see cref="StatusIds"/>).</summary>
    public Guid StatusId { get; set; }

    public bool IsActive => StatusId == StatusIds.Active;

    public int FailedVerificationCount { get; set; }

    public DateTime? LockedUntil { get; set; }
}
