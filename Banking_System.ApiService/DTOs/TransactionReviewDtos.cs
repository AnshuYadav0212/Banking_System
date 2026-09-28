namespace Banking_System.ApiService.DTOs;

/// <summary>One row of the employee transaction-review queue.</summary>
public sealed record TransactionQueueRow(
    Guid TransactionId,
    DateTime CreatedAt,
    string FromAccountNumber,
    string ToAccountNumber,
    string FromCustomerName,
    string ToCustomerName,
    decimal Amount,
    string? Comment,
    string Status,
    string? InitiatedByUsername,
    string? AssignedToUsername,
    bool LockedByAnotherEmployee,
    DateTime? ReviewLockExpiresAt);

/// <summary>Full detail of one transaction, for an employee to decide on before approving or rejecting it.</summary>
public sealed record TransactionReviewDetail(
    Guid TransactionId,
    DateTime CreatedAt,
    decimal Amount,
    string? Comment,
    string Status,
    string FromAccountNumber,
    string FromCustomerName,
    decimal FromAccountBalance,
    decimal FromCustomerTotalBalance,
    bool FromAccountActive,
    bool FromCustomerActive,
    string ToAccountNumber,
    string ToCustomerName,
    decimal ToAccountBalance,
    decimal ToCustomerTotalBalance,
    bool ToAccountActive,
    bool ToCustomerActive,
    string? InitiatedByUsername,
    string? AssignedToUsername,
    bool LockedByAnotherEmployee,
    bool HeldByMe,
    DateTime? ReviewLockExpiresAt)
{
    /// <summary>The sender's balance would go negative, or below what an unrelated approval already reserved - a clear reason to reject.</summary>
    public bool InsufficientFunds => FromAccountBalance < Amount;
}

public sealed record TransactionQueuePage(
    IReadOnlyList<TransactionQueueRow> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int PendingCount,
    decimal PendingTotal,
    int ProcessingCount,
    int CompletedCount,
    int RejectedCount);
