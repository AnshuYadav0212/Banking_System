namespace Banking_System.Web.Models.Transactions;

// Mirrors Banking_System.ApiService/DTOs/TransactionReviewDtos.cs.
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
    public bool InsufficientFunds => FromAccountBalance < Amount;
}

public sealed record TransactionQueuePage(
    List<TransactionQueueRow> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int PendingCount,
    decimal PendingTotal,
    int ProcessingCount,
    int CompletedCount,
    int RejectedCount);
