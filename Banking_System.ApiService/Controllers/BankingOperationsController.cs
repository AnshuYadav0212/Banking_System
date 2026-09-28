using System.Security.Claims;
using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.ApiService.Controllers;

/// <summary>Staff-only: freeze/unfreeze an account, restrict/restore a customer profile.</summary>
[ApiController]
[Authorize(Policy = "EmployeeOrAdmin")]
[Route("api/operations")]
public sealed class BankingOperationsController : ControllerBase
{
    private readonly BankingOperationsService _operations;

    public BankingOperationsController(BankingOperationsService operations)
    {
        _operations = operations;
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> SearchAccounts([FromQuery] string? q, CancellationToken cancellationToken) =>
        Ok(await _operations.SearchAccountsAsync(q, cancellationToken));

    [HttpPost("accounts/{accountId:guid}/status")]
    public async Task<IActionResult> SetAccountStatus(
        Guid accountId, SetStatusRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var applied = await _operations.SetAccountStatusAsync(accountId, request.Active, userId, cancellationToken);

        return applied ? Ok() : NotFound();
    }

    [HttpGet("customers")]
    public async Task<IActionResult> SearchCustomers([FromQuery] string? q, CancellationToken cancellationToken) =>
        Ok(await _operations.SearchCustomersAsync(q, cancellationToken));

    [HttpPost("customers/{customerId:guid}/status")]
    public async Task<IActionResult> SetCustomerStatus(
        Guid customerId, SetStatusRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var applied = await _operations.SetCustomerStatusAsync(customerId, request.Active, userId, cancellationToken);

        return applied ? Ok() : NotFound();
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
