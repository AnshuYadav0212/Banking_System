using System.Security.Claims;
using Banking_System.ApiService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.ApiService.Controllers;

/// <summary>Employee review queue for transfers that needed approval (₹1,00,000 or more).</summary>
[ApiController]
[Authorize(Policy = "EmployeeOrAdmin")]
[Route("api/transactions")]
public sealed class TransactionReviewController : ControllerBase
{
    private readonly TransactionReviewService _reviews;

    public TransactionReviewController(TransactionReviewService reviews)
    {
        _reviews = reviews;
    }

    [HttpGet("queue")]
    public async Task<IActionResult> Queue(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        // "to" is a calendar day from the picker; include the whole day.
        var toExclusive = to?.Date.AddDays(1);

        var result = await _reviews.SearchQueueAsync(userId, q, status, from?.Date, toExclusive, page, pageSize, cancellationToken);

        return Ok(result);
    }

    [HttpGet("{transactionId:guid}")]
    public async Task<IActionResult> Detail(Guid transactionId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var detail = await _reviews.GetDetailAsync(userId, transactionId, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("{transactionId:guid}/start-review")]
    public Task<IActionResult> StartReview(Guid transactionId, CancellationToken cancellationToken) =>
        RunActionAsync(transactionId, (userId, ct) => _reviews.StartReviewAsync(transactionId, userId, ct), cancellationToken);

    [HttpPost("{transactionId:guid}/approve")]
    public Task<IActionResult> Approve(Guid transactionId, CancellationToken cancellationToken) =>
        RunActionAsync(transactionId, (userId, ct) => _reviews.ApproveAsync(transactionId, userId, ct), cancellationToken);

    [HttpPost("{transactionId:guid}/reject")]
    public Task<IActionResult> Reject(Guid transactionId, CancellationToken cancellationToken) =>
        RunActionAsync(transactionId, (userId, ct) => _reviews.RejectAsync(transactionId, userId, ct), cancellationToken);

    private async Task<IActionResult> RunActionAsync(
        Guid transactionId, Func<Guid, CancellationToken, Task<TransactionReviewResult>> action, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await action(userId, cancellationToken);

        return result.Status switch
        {
            TransactionReviewStatus.Success => Ok(),
            TransactionReviewStatus.NotFound => NotFound(),
            TransactionReviewStatus.InvalidTransition => Conflict(new { error = "InvalidTransition" }),
            TransactionReviewStatus.LockedByAnotherEmployee => Conflict(new { error = "Locked" }),
            TransactionReviewStatus.InsufficientFunds => UnprocessableEntity(new { error = "InsufficientFunds" }),
            TransactionReviewStatus.AccountInactive => UnprocessableEntity(new { error = "AccountInactive" }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
