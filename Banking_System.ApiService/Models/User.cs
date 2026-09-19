namespace Banking_System.ApiService.Models;

/// <summary>An online-banking login.</summary>
public sealed class User
{
    public Guid UserId { get; set; }

    /// <summary>The bank customer this login belongs to; null for logins that pre-date registration by bank record.</summary>
    public Guid? BankCustomerId { get; set; }

    public string? Username { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Key into the Roles table (see <see cref="RoleIds"/>).</summary>
    public Guid RoleId { get; set; }

    /// <summary>Key into the Statuses table (see <see cref="StatusIds"/>).</summary>
    public Guid StatusId { get; set; } = StatusIds.Active;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsActive => StatusId == StatusIds.Active;

    // Not stored on the Users table. The role's name comes from the Roles
    // lookup; the rest belong to the bank customer. Both are filled in by
    // joins when a login is read (customer details are null when the login is
    // not linked to a customer).
    public string Role { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? PhoneNumber { get; set; }
}
