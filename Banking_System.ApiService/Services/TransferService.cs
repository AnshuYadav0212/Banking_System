using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;

namespace Banking_System.ApiService.Services;

public enum TransferStatus
{
    Completed,
    /// <summary>The same request id was already used for this exact transfer; the original result is returned.</summary>
    AlreadyProcessed,
    /// <summary>The caller's login is inactive, or is not linked to a bank customer.</summary>
    SenderNotAllowed,
    /// <summary>The account to send from does not exist or is not the caller's.</summary>
    AccountNotFound,
    SenderAccountInactive,
    SameAccount,
    /// <summary>Not found, not registered for online banking, or inactive: deliberately one answer.</summary>
    RecipientUnavailable,
    InsufficientFunds,
    /// <summary>The request id belongs to a different transfer.</summary>
    RequestIdConflict
}

/// <summary><see cref="Reason"/> is for developers and logs; it is never shown to the customer.</summary>
public sealed record TransferResult(
    TransferStatus Status,
    TransferResponse? Response = null,
    string? Reason = null);

/// <summary>
/// Sending money between accounts. The customer must be an active online-banking
/// user sending from one of their own active accounts, the receiving account must
/// belong to a customer who has an active online-banking login, both accounts and
/// both customers must be active, and the balance must cover the amount.
/// </summary>
public sealed class TransferService
{
    private const int MaxCommentLength = 200;

    private readonly IUserRepository _users;
    private readonly IBankCustomerRepository _customers;
    private readonly IBankAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly ILogger<TransferService> _logger;

    public TransferService(
        IUserRepository users,
        IBankCustomerRepository customers,
        IBankAccountRepository accounts,
        ITransactionRepository transactions,
        ILogger<TransferService> logger)
    {
        _users = users;
        _customers = customers;
        _accounts = accounts;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AccountSummary>> GetMyAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);

        if (user is not { IsActive: true, BankCustomerId: { } customerId })
        {
            return [];
        }

        var accounts = await _accounts.GetByCustomerAsync(customerId, cancellationToken);

        return accounts
            .Select(a => new AccountSummary(a.AccountId, a.AccountNumber, a.AvailableBalance, a.IsActive))
            .ToList();
    }

    public async Task<IReadOnlyList<TransactionSummary>> GetHistoryAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);

        if (user is not { IsActive: true, BankCustomerId: { } customerId })
        {
            return [];
        }

        var records = await _transactions.GetForCustomerAsync(customerId, take, cancellationToken);

        return records.Select(r =>
        {
            var sent = r.FromCustomerId == customerId;
            var received = r.ToCustomerId == customerId;

            // A transfer between two of the customer's own accounts is shown as such.
            var direction = sent && received ? "Own transfer" : sent ? "Sent" : "Received";
            var counterparty = sent ? r.ToAccountNumber : r.FromAccountNumber;
            var counterpartyName = sent
                ? DisplayName(r.ToFirstName, r.ToLastName)
                : DisplayName(r.FromFirstName, r.FromLastName);

            return new TransactionSummary(
                r.TransactionId, r.CreatedAt, direction, counterparty, counterpartyName, r.Amount, r.Comment);
        }).ToList();
    }

    /// <summary>
    /// Looks up the account holder's name for an account number, so the sender can
    /// confirm who they are paying before the amount is even entered. Returns null
    /// for anything that could not receive a transfer anyway (not found, not
    /// online, inactive) - deliberately one answer, same as a failed transfer.
    /// </summary>
    public async Task<RecipientVerification?> VerifyRecipientAsync(
        Guid userId,
        string accountNumber,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);

        if (user is not { IsActive: true, BankCustomerId: not null })
        {
            return null;
        }

        var recipient = await _accounts.FindRecipientAsync(accountNumber, cancellationToken);

        if (recipient is not { AccountIsActive: true, CustomerIsActive: true, HasActiveLogin: true })
        {
            return null;
        }

        return new RecipientVerification(recipient.AccountNumber, DisplayName(recipient));
    }

    public async Task<TransferResult> TransferAsync(
        Guid userId,
        TransferRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);

        // The token may outlive the login, so the current state is re-checked.
        if (user is not { IsActive: true, BankCustomerId: { } customerId })
        {
            return Fail(TransferStatus.SenderNotAllowed, "the login is inactive or not linked to a bank customer");
        }

        var sender = await _accounts.GetByIdAsync(request.FromAccountId, cancellationToken);

        if (sender is null || sender.CustomerId != customerId)
        {
            return Fail(TransferStatus.AccountNotFound, "the account to send from is not one of the caller's accounts");
        }

        var amount = request.Amount;
        var comment = CleanComment(request.Comment);

        // A repeated submit of the very same form returns the original result
        // instead of moving the money a second time.
        var existing = await _transactions.GetByRequestIdAsync(request.RequestId, cancellationToken);

        if (existing is not null)
        {
            return await ReplayAsync(existing, userId, sender, request, cancellationToken);
        }

        var recipient = await _accounts.FindRecipientAsync(request.ToAccountNumber, cancellationToken);

        if (recipient is null)
        {
            return Fail(TransferStatus.RecipientUnavailable, "no account has that number");
        }

        if (recipient.AccountId == sender.AccountId)
        {
            return Fail(TransferStatus.SameAccount, "sending to the same account");
        }

        if (!recipient.AccountIsActive)
        {
            return Fail(TransferStatus.RecipientUnavailable, "the receiving account is not active");
        }

        if (!recipient.CustomerIsActive)
        {
            return Fail(TransferStatus.RecipientUnavailable, "the receiving customer is not active");
        }

        if (!recipient.HasActiveLogin)
        {
            return Fail(TransferStatus.RecipientUnavailable, "the receiving customer has no active online-banking login");
        }

        var senderCustomer = await _customers.GetByIdAsync(customerId, cancellationToken);

        if (!sender.IsActive || senderCustomer is not { IsActive: true })
        {
            return Fail(TransferStatus.SenderAccountInactive, "the sending account or its customer is not active");
        }

        if (sender.AvailableBalance < amount)
        {
            return Fail(TransferStatus.InsufficientFunds, "the available balance is lower than the amount");
        }

        var outcome = await _transactions.TransferAsync(
            new TransferCommand(request.RequestId, sender.AccountId, recipient.AccountId, amount, comment, userId),
            cancellationToken);

        switch (outcome.Status)
        {
            case TransferOutcomeStatus.Completed:
                _logger.LogInformation(
                    "Transfer {TransactionId}: {Amount} from account ending {From} to account ending {To}.",
                    outcome.TransactionId, amount, Ending(sender.AccountNumber), Ending(recipient.AccountNumber));

                return new TransferResult(
                    TransferStatus.Completed,
                    new TransferResponse(
                        outcome.TransactionId!.Value,
                        outcome.CreatedAt!.Value,
                        amount,
                        sender.AccountNumber,
                        recipient.AccountNumber,
                        DisplayName(recipient),
                        outcome.SenderBalanceAfter!.Value));

            case TransferOutcomeStatus.InsufficientFunds:
                return Fail(TransferStatus.InsufficientFunds, "the balance changed and no longer covers the amount");

            case TransferOutcomeStatus.AccountInactive:
                return Fail(TransferStatus.RecipientUnavailable, "an account became inactive during the transfer");

            case TransferOutcomeStatus.DuplicateRequest:
                var winner = await _transactions.GetByRequestIdAsync(request.RequestId, cancellationToken);

                return winner is null
                    ? Fail(TransferStatus.RequestIdConflict, "the request id was used at the same moment")
                    : await ReplayAsync(winner, userId, sender, request, cancellationToken);

            default:
                return Fail(TransferStatus.AccountNotFound, "an account no longer exists");
        }
    }

    private async Task<TransferResult> ReplayAsync(
        TransactionRecord existing,
        Guid userId,
        BankAccount sender,
        TransferRequest request,
        CancellationToken cancellationToken)
    {
        // Only the same person repeating the same transfer gets the original back.
        if (existing.InitiatedByUserId != userId
            || existing.FromAccountId != sender.AccountId
            || existing.ToAccountNumber != request.ToAccountNumber
            || existing.Amount != request.Amount)
        {
            return Fail(TransferStatus.RequestIdConflict, "the request id belongs to a different transfer");
        }

        var current = await _accounts.GetByIdAsync(sender.AccountId, cancellationToken);
        var recipient = await _accounts.FindRecipientAsync(existing.ToAccountNumber, cancellationToken);

        return new TransferResult(
            TransferStatus.AlreadyProcessed,
            new TransferResponse(
                existing.TransactionId,
                existing.CreatedAt,
                existing.Amount,
                existing.FromAccountNumber,
                existing.ToAccountNumber,
                recipient is null ? string.Empty : DisplayName(recipient),
                current?.AvailableBalance ?? 0m));
    }

    private TransferResult Fail(TransferStatus status, string reason)
    {
        _logger.LogInformation("Transfer not made ({Status}): {Reason}.", status, reason);

        return new TransferResult(status, Reason: reason);
    }

    // "Priya S." - enough for the sender to recognise the person, without
    // revealing the full name of whoever owns an account number.
    private static string DisplayName(RecipientLookup recipient) =>
        DisplayName(recipient.FirstName, recipient.LastName);

    private static string DisplayName(string firstName, string lastName)
    {
        var last = lastName.Trim();

        return last.Length == 0
            ? firstName
            : $"{firstName} {char.ToUpperInvariant(last[0])}.";
    }

    private static string? CleanComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var cleaned = new string(comment.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (cleaned.Length > MaxCommentLength)
        {
            cleaned = cleaned[..MaxCommentLength];
        }

        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string Ending(string accountNumber) =>
        accountNumber.Length <= 4 ? "****" : "..." + accountNumber[^4..];
}
