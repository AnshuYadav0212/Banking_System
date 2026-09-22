using System.Security.Claims;
using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;
using Banking_System.ApiService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.ApiService.Controllers;

/// <summary>Customer support tickets: customers raise them, employees/admins work them.</summary>
[ApiController]
[Authorize]
[Route("api/tickets")]
public sealed class SupportTicketsController : ControllerBase
{
    private readonly SupportTicketService _tickets;
    private readonly IUserRepository _users;

    public SupportTicketsController(SupportTicketService tickets, IUserRepository users)
    {
        _tickets = tickets;
        _users = users;
    }

    [HttpPost]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> Create(CreateTicketRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _tickets.CreateAsync(userId, request, cancellationToken);

        return result.Status switch
        {
            CreateTicketStatus.Created => Created($"/api/tickets/{result.TicketId}", new { ticketId = result.TicketId }),
            CreateTicketStatus.CustomerNotAllowed => Forbid(),
            CreateTicketStatus.InvalidCategory => BadRequest(new { error = "InvalidCategory" }),
            CreateTicketStatus.TransactionNotFound => BadRequest(new { error = "TransactionNotFound" }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("mine")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> Mine([FromQuery] Guid? status, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _users.GetByIdAsync(userId, cancellationToken);

        if (user?.BankCustomerId is not { } customerId)
        {
            return Ok(Array.Empty<TicketSummary>());
        }

        var tickets = await _tickets.GetForCustomerAsync(customerId, status, cancellationToken);

        return Ok(tickets.Select(ToSummary).ToList());
    }

    [HttpGet("queue")]
    [Authorize(Policy = "EmployeeOrAdmin")]
    public async Task<IActionResult> Queue(
        [FromQuery] Guid? status, [FromQuery] Guid? category, CancellationToken cancellationToken)
    {
        var tickets = await _tickets.GetQueueAsync(status, category, cancellationToken);

        return Ok(tickets.Select(ToSummary).ToList());
    }

    [HttpGet("{ticketId:guid}")]
    public async Task<IActionResult> Detail(Guid ticketId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var detail = await _tickets.GetDetailAsync(userId, IsStaff(), ticketId, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("{ticketId:guid}/start-review")]
    [Authorize(Policy = "EmployeeOrAdmin")]
    public Task<IActionResult> StartReview(Guid ticketId, CancellationToken cancellationToken) =>
        RunActionAsync(ticketId, (employeeId, ct) => _tickets.StartReviewAsync(employeeId, ticketId, ct), cancellationToken);

    [HttpPost("{ticketId:guid}/resolve")]
    [Authorize(Policy = "EmployeeOrAdmin")]
    public Task<IActionResult> Resolve(Guid ticketId, TicketActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Task.FromResult<IActionResult>(BadRequest(new { error = "NoteRequired" }));
        }

        return RunActionAsync(ticketId, (employeeId, ct) => _tickets.ResolveAsync(employeeId, ticketId, request.Note.Trim(), ct), cancellationToken);
    }

    [HttpPost("{ticketId:guid}/reject")]
    [Authorize(Policy = "EmployeeOrAdmin")]
    public Task<IActionResult> Reject(Guid ticketId, TicketActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Task.FromResult<IActionResult>(BadRequest(new { error = "NoteRequired" }));
        }

        return RunActionAsync(ticketId, (employeeId, ct) => _tickets.RejectAsync(employeeId, ticketId, request.Note.Trim(), ct), cancellationToken);
    }

    [HttpPost("{ticketId:guid}/close")]
    [Authorize(Policy = "EmployeeOrAdmin")]
    public Task<IActionResult> Close(Guid ticketId, TicketActionRequest request, CancellationToken cancellationToken) =>
        RunActionAsync(ticketId, (employeeId, ct) => _tickets.CloseAsync(employeeId, ticketId, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), ct), cancellationToken);

    private async Task<IActionResult> RunActionAsync(
        Guid ticketId, Func<Guid, CancellationToken, Task<TicketActionResult>> action, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var employeeId))
        {
            return Unauthorized();
        }

        var result = await action(employeeId, cancellationToken);

        return result.Status switch
        {
            TicketActionStatus.Success => Ok(),
            TicketActionStatus.NotFound => NotFound(),
            TicketActionStatus.InvalidTransition => Conflict(new { error = "InvalidTransition" }),
            TicketActionStatus.Conflict => Conflict(new { error = "Conflict" }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static TicketSummary ToSummary(SupportTicket t) => new(
        t.TicketId, t.Category, t.Priority, t.Status, t.Subject, t.TransactionId, t.AssignedToUsername, t.CreatedAt, t.UpdatedAt);

    private bool IsStaff() => User.IsInRole("Employee") || User.IsInRole("Admin");

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
