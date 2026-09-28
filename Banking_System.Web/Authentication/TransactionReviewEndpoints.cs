using Banking_System.Web.Services.Authentication;
using Banking_System.Web.Services.Transactions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.Web.Authentication;

/// <summary>
/// The employee actions on a queued transaction (start review / approve / reject)
/// are plain HTML form posts, not Blazor event handlers - the queue page is static
/// server-rendered (no @@rendermode). Mirrors TicketEndpoints.
/// </summary>
public static class TransactionReviewEndpoints
{
    public static IEndpointRouteBuilder MapTransactionReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var staff = new AuthorizeAttribute { Roles = "Employee,Admin" };

        endpoints.MapPost("/employee/transactions/{transactionId:guid}/start-review", (Delegate)StartReviewAsync).RequireAuthorization(staff);
        endpoints.MapPost("/employee/transactions/{transactionId:guid}/approve", (Delegate)ApproveAsync).RequireAuthorization(staff);
        endpoints.MapPost("/employee/transactions/{transactionId:guid}/reject", (Delegate)RejectAsync).RequireAuthorization(staff);

        return endpoints;
    }

    private static Task<IResult> StartReviewAsync(
        Guid transactionId, [FromForm] string? returnUrl, TransactionReviewApiClient reviews, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        RunAsync(returnUrl, httpContext, antiforgery, (token, ct) => reviews.StartReviewAsync(token, transactionId, ct), cancellationToken);

    private static Task<IResult> ApproveAsync(
        Guid transactionId, [FromForm] string? returnUrl, TransactionReviewApiClient reviews, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        RunAsync(returnUrl, httpContext, antiforgery, (token, ct) => reviews.ApproveAsync(token, transactionId, ct), cancellationToken);

    private static Task<IResult> RejectAsync(
        Guid transactionId, [FromForm] string? returnUrl, TransactionReviewApiClient reviews, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        RunAsync(returnUrl, httpContext, antiforgery, (token, ct) => reviews.RejectAsync(token, transactionId, ct), cancellationToken);

    private static async Task<IResult> RunAsync(
        string? returnUrl,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        Func<string, CancellationToken, Task<ApiResult<NoData>>> action,
        CancellationToken cancellationToken)
    {
        // Only ever redirects back into our own queue page, never to an external URL.
        var back = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//")
            ? "/employee/transactions"
            : returnUrl;

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

        var separator = back.Contains('?') ? "&" : "?";

        try
        {
            var result = await action(token, cancellationToken);

            return Results.Redirect(result.Success
                ? $"{back}{separator}ok=1"
                : $"{back}{separator}error={Uri.EscapeDataString(result.Error ?? "failed")}");
        }
        catch (HttpRequestException)
        {
            return Results.Redirect($"{back}{separator}error=unavailable");
        }
    }
}
