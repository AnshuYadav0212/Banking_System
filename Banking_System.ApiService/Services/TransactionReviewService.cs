using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Formatting;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;

namespace Banking_System.ApiService.Services;

public enum TransactionReviewStatus
{
    Success,
    NotFound,
    InvalidTransition,
    LockedByAnotherEmployee,
    InsufficientFunds,
    AccountInactive
}

public sealed record TransactionReviewResult(TransactionReviewStatus Status);

/// <summary>
/// Employee review of large ("needs approval") transfers: Pending -&gt; Processing
/// (a short review lease, same pattern as ticket review) -&gt; Completed or Rejected.
/// Approving is the only place real money moves for one of these transfers.
/// </summary>
public sealed class TransactionReviewService
{
    // Mirrors SupportTicketService.ReviewLockSeconds: how long one employee's
    // review lease lasts before another employee may take the transaction over.
    private const int ReviewLockSeconds = 60;

    private readonly ITransactionRepository _transactions;
    private readonly ILogger<TransactionReviewService> _logger;

    public TransactionReviewService(ITransactionRepository transactions, ILogger<TransactionReviewService> logger)
    {
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<TransactionQueuePage> SearchQueueAsync(
        Guid viewerUserId, string? search, string? status, DateTime? from, DateTime? to, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var entries = await _transactions.SearchQueueAsync(search, status, from, to, page, pageSize, cancellationToken);
        var counts = await _transactions.GetStatusCountsAsync(cancellationToken);

        var items = entries.Select(e => new TransactionQueueRow(
            e.TransactionId,
            e.CreatedAt,
            AccountNumberMasking.Mask(e.FromAccountNumber),
            AccountNumberMasking.Mask(e.ToAccountNumber),
            e.FromCustomerName,
            e.ToCustomerName,
            e.Amount,
            e.Comment,
            e.Status,
            e.InitiatedByUsername,
            e.AssignedToUsername,
            IsLocked(e, viewerUserId),
            e.ReviewLockExpiresAt)).ToList();

        var total = entries.Count > 0 ? entries[0].TotalCount : 0;

        return new TransactionQueuePage(
            items, total, page, pageSize,
            counts.PendingCount, counts.PendingTotal, counts.ProcessingCount, counts.CompletedCount, counts.RejectedCount);
    }

    public async Task<TransactionReviewDetail?> GetDetailAsync(
        Guid viewerUserId, Guid transactionId, CancellationToken cancellationToken)
    {
        var e = await _transactions.GetReviewDetailAsync(transactionId, cancellationToken);

        if (e is null)
        {
            return null;
        }

        var locked = e.Status == "Processing"
            && e.AssignedToUserId is { } holder && holder != viewerUserId
            && e.ReviewLockExpiresAt is { } expires
            && DateTime.SpecifyKind(expires, DateTimeKind.Utc) > DateTime.UtcNow;

        var heldByMe = e.Status == "Processing" && e.AssignedToUserId == viewerUserId;

        return new TransactionReviewDetail(
            e.TransactionId,
            e.CreatedAt,
            e.Amount,
            e.Comment,
            e.Status,
            AccountNumberMasking.Mask(e.FromAccountNumber),
            e.FromCustomerName,
            e.FromAccountBalance,
            e.FromCustomerTotalBalance,
            e.FromAccountActive,
            e.FromCustomerActive,
            AccountNumberMasking.Mask(e.ToAccountNumber),
            e.ToCustomerName,
            e.ToAccountBalance,
            e.ToCustomerTotalBalance,
            e.ToAccountActive,
            e.ToCustomerActive,
            e.InitiatedByUsername,
            e.AssignedToUsername,
            locked,
            heldByMe,
            e.ReviewLockExpiresAt);
    }

    public async Task<TransactionReviewResult> StartReviewAsync(
        Guid transactionId, Guid employeeUserId, CancellationToken cancellationToken)
    {
        var outcome = await _transactions.StartReviewAsync(transactionId, employeeUserId, ReviewLockSeconds, cancellationToken);

        if (outcome == TransactionReviewOutcome.Applied)
        {
            _logger.LogInformation("Transaction {TransactionId} taken for review by user {UserId}.", transactionId, employeeUserId);
        }

        return ToResult(outcome);
    }

    public async Task<TransactionReviewResult> ApproveAsync(
        Guid transactionId, Guid employeeUserId, CancellationToken cancellationToken)
    {
        var outcome = await _transactions.ApproveAsync(transactionId, employeeUserId, cancellationToken);

        if (outcome == TransactionReviewOutcome.Applied)
        {
            _logger.LogInformation("Transaction {TransactionId} approved by user {UserId}.", transactionId, employeeUserId);
        }

        return ToResult(outcome);
    }

    public async Task<TransactionReviewResult> RejectAsync(
        Guid transactionId, Guid employeeUserId, CancellationToken cancellationToken)
    {
        var outcome = await _transactions.RejectAsync(transactionId, employeeUserId, cancellationToken);

        if (outcome == TransactionReviewOutcome.Applied)
        {
            _logger.LogInformation("Transaction {TransactionId} rejected by user {UserId}.", transactionId, employeeUserId);
        }

        return ToResult(outcome);
    }

    private static bool IsLocked(TransactionQueueEntry e, Guid viewerUserId) =>
        e.Status == "Processing"
        && e.AssignedToUserId is { } holder && holder != viewerUserId
        && e.ReviewLockExpiresAt is { } expires
        && DateTime.SpecifyKind(expires, DateTimeKind.Utc) > DateTime.UtcNow;

    private static TransactionReviewResult ToResult(TransactionReviewOutcome outcome) => outcome switch
    {
        TransactionReviewOutcome.Applied => new TransactionReviewResult(TransactionReviewStatus.Success),
        TransactionReviewOutcome.LockedByAnotherEmployee => new TransactionReviewResult(TransactionReviewStatus.LockedByAnotherEmployee),
        TransactionReviewOutcome.InsufficientFunds => new TransactionReviewResult(TransactionReviewStatus.InsufficientFunds),
        TransactionReviewOutcome.AccountInactive => new TransactionReviewResult(TransactionReviewStatus.AccountInactive),
        _ => new TransactionReviewResult(TransactionReviewStatus.InvalidTransition)
    };
}
