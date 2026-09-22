using Banking_System.Web.Services.Authentication;
using Banking_System.Web.Services.Tickets;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.Web.Authentication;

/// <summary>
/// The employee actions on a ticket (start review / resolve / reject / close) are
/// plain HTML form posts, not Blazor event handlers - the ticket detail page is
/// static server-rendered (no @@rendermode), so a bare @@onclick would never run.
/// This mirrors how /auth/logout already works from the nav menu.
/// </summary>
public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var staff = new AuthorizeAttribute { Roles = "Employee,Admin" };

        endpoints.MapPost("/tickets/{ticketId:guid}/start-review", (Delegate)StartReviewAsync).RequireAuthorization(staff);
        endpoints.MapPost("/tickets/{ticketId:guid}/resolve", (Delegate)ResolveAsync).RequireAuthorization(staff);
        endpoints.MapPost("/tickets/{ticketId:guid}/reject", (Delegate)RejectAsync).RequireAuthorization(staff);
        endpoints.MapPost("/tickets/{ticketId:guid}/close", (Delegate)CloseAsync).RequireAuthorization(staff);

        return endpoints;
    }

    private static Task<IResult> StartReviewAsync(
        Guid ticketId, TicketApiClient tickets, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        RunAsync(ticketId, httpContext, antiforgery, (token, ct) => tickets.StartReviewAsync(token, ticketId, ct), cancellationToken);

    private static Task<IResult> ResolveAsync(
        Guid ticketId, [FromForm] string? note, TicketApiClient tickets, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return Task.FromResult(Results.Redirect($"/tickets/{ticketId}?error=note-required"));
        }

        return RunAsync(ticketId, httpContext, antiforgery, (token, ct) => tickets.ResolveAsync(token, ticketId, note.Trim(), ct), cancellationToken);
    }

    private static Task<IResult> RejectAsync(
        Guid ticketId, [FromForm] string? note, TicketApiClient tickets, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return Task.FromResult(Results.Redirect($"/tickets/{ticketId}?error=note-required"));
        }

        return RunAsync(ticketId, httpContext, antiforgery, (token, ct) => tickets.RejectAsync(token, ticketId, note.Trim(), ct), cancellationToken);
    }

    private static Task<IResult> CloseAsync(
        Guid ticketId, [FromForm] string? note, TicketApiClient tickets, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        RunAsync(ticketId, httpContext, antiforgery, (token, ct) => tickets.CloseAsync(token, ticketId, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), ct), cancellationToken);

    private static async Task<IResult> RunAsync(
        Guid ticketId,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        Func<string, CancellationToken, Task<ApiResult<NoData>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Invalid antiforgery token.");
        }

        var token = await httpContext.GetTokenAsync(BankingTokens.AccessToken);

        if (string.IsNullOrEmpty(token))
        {
            return Results.Redirect("/login");
        }

        try
        {
            var result = await action(token, cancellationToken);

            return Results.Redirect(result.Success
                ? $"/tickets/{ticketId}?ok=1"
                : $"/tickets/{ticketId}?error={Uri.EscapeDataString(result.Error ?? "failed")}");
        }
        catch (HttpRequestException)
        {
            return Results.Redirect($"/tickets/{ticketId}?error=unavailable");
        }
    }
}
