using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Formatting;
using Banking_System.ApiService.Repositories;

namespace Banking_System.ApiService.Services;

/// <summary>
/// The two direct staff actions on live banking access: freezing/unfreezing one
/// account, and restricting/restoring a customer's whole profile. Both take
/// effect immediately through the same StatusId checks every other flow
/// (login, transfer, registration) already uses.
/// </summary>
public sealed class BankingOperationsService
{
    private const int MaxResults = 20;
    private const int MinSearchLength = 2;

    private readonly IBankingOperationsRepository _operations;
    private readonly ILogger<BankingOperationsService> _logger;

    public BankingOperationsService(IBankingOperationsRepository operations, ILogger<BankingOperationsService> logger)
    {
        _operations = operations;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AccountOperationRow>> SearchAccountsAsync(
        string? search, CancellationToken cancellationToken)
    {
        if (search is null || search.Trim().Length < MinSearchLength)
        {
            return [];
        }

        var entries = await _operations.SearchAccountsAsync(search, MaxResults, cancellationToken);

        return entries.Select(e => new AccountOperationRow(
            e.AccountId,
            AccountNumberMasking.Mask(e.AccountNumber),
            e.CustomerName,
            SensitiveMasking.MaskNumber(e.CifNumber),
            e.AvailableBalance,
            e.Status == "Active")).ToList();
    }

    public async Task<bool> SetAccountStatusAsync(
        Guid accountId, bool active, Guid employeeUserId, CancellationToken cancellationToken)
    {
        var applied = await _operations.SetAccountStatusAsync(accountId, active, cancellationToken);

        if (applied)
        {
            _logger.LogInformation(
                "Account {AccountId} {State} by user {UserId}.",
                accountId, active ? "unfrozen" : "frozen", employeeUserId);
        }

        return applied;
    }

    public async Task<IReadOnlyList<CustomerOperationRow>> SearchCustomersAsync(
        string? search, CancellationToken cancellationToken)
    {
        if (search is null || search.Trim().Length < MinSearchLength)
        {
            return [];
        }

        var entries = await _operations.SearchCustomersAsync(search, MaxResults, cancellationToken);

        return entries.Select(e => new CustomerOperationRow(
            e.CustomerId,
            SensitiveMasking.MaskNumber(e.CifNumber),
            e.Name,
            SensitiveMasking.MaskNumber(e.PhoneNumber),
            e.Status == "Active",
            e.AccountCount)).ToList();
    }

    public async Task<bool> SetCustomerStatusAsync(
        Guid customerId, bool active, Guid employeeUserId, CancellationToken cancellationToken)
    {
        var applied = await _operations.SetCustomerStatusAsync(customerId, active, cancellationToken);

        if (applied)
        {
            _logger.LogInformation(
                "Customer {CustomerId} {State} by user {UserId}.",
                customerId, active ? "restored" : "restricted", employeeUserId);
        }

        return applied;
    }
}
