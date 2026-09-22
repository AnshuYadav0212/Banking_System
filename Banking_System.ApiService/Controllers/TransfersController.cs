using System.Security.Claims;
using System.Text.RegularExpressions;
using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Banking_System.ApiService.Controllers;

/// <summary>Accounts and money transfers of the signed-in customer.</summary>
[ApiController]
[Authorize(Policy = "CustomerOnly")]
[Route("api")]
public sealed class TransfersController : ControllerBase
{
    private const int HistoryPageSize = 20;
    private const int MaxHistoryPageSize = 300;

    private readonly TransferService _transfers;
    private readonly IHostEnvironment _environment;

    public TransfersController(TransferService transfers, IHostEnvironment environment)
    {
        _transfers = transfers;
        _environment = environment;
    }

    [HttpGet("accounts/mine")]
    public async Task<IActionResult> MyAccounts(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await _transfers.GetMyAccountsAsync(userId, cancellationToken));
    }

    [HttpGet("transfers")]
    public async Task<IActionResult> History([FromQuery] int? take, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var pageSize = Math.Clamp(take ?? HistoryPageSize, 1, MaxHistoryPageSize);

        return Ok(await _transfers.GetHistoryAsync(userId, pageSize, cancellationToken));
    }

    [HttpGet("accounts/verify/{accountNumber}")]
    [EnableRateLimiting("recipient-lookup")]
    public async Task<IActionResult> VerifyRecipient(string accountNumber, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!Regex.IsMatch(accountNumber, "^[0-9]{6,20}$"))
        {
            return NotFound(new { error = "RecipientUnavailable" });
        }

        var recipient = await _transfers.VerifyRecipientAsync(userId, accountNumber, cancellationToken);

        return recipient is null
            ? NotFound(new { error = "RecipientUnavailable" })
            : Ok(recipient);
    }

    [HttpPost("transfers")]
    public async Task<IActionResult> Transfer(
        TransferRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _transfers.TransferAsync(userId, request, cancellationToken);

        return result.Status switch
        {
            TransferStatus.Completed =>
                Created($"/api/transfers/{result.Response!.TransactionId}", result.Response),
            TransferStatus.AlreadyProcessed => Ok(result.Response),
            TransferStatus.SenderNotAllowed => Forbid(),
            TransferStatus.AccountNotFound =>
                NotFound(new { error = "AccountNotFound" }),
            TransferStatus.SenderAccountInactive =>
                UnprocessableEntity(new { error = "SenderAccountInactive" }),
            TransferStatus.SameAccount =>
                UnprocessableEntity(new { error = "SameAccount" }),
            TransferStatus.InsufficientFunds =>
                UnprocessableEntity(new { error = "InsufficientFunds" }),
            TransferStatus.RecipientUnavailable =>
                // One answer for "no such account", "not registered online" and
                // "inactive", so this cannot be used to probe accounts. In
                // Development the real reason is included to help diagnose it.
                UnprocessableEntity(new
                {
                    error = "RecipientUnavailable",
                    hint = _environment.IsDevelopment() ? result.Reason : null
                }),
            TransferStatus.RequestIdConflict =>
                Conflict(new { error = "RequestIdConflict" }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
