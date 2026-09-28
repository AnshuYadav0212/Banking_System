using Banking_System.Web.Services.Authentication;
using Banking_System.Web.Services.Operations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.Web.Authentication;

/// <summary>
/// Freeze/unfreeze an account and restrict/restore a customer are plain HTML form
/// posts, not Blazor event handlers - the operations page is static server-rendered.
/// Mirrors TicketEndpoints and TransactionReviewEndpoints.
/// </summary>
public static class BankingOperationsEndpoints
{
    public static IEndpointRouteBuilder MapBankingOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var staff = new AuthorizeAttribute { Roles = "Employee,Admin" };

        endpoints.MapPost("/employee/operations/accounts/{accountId:guid}/status", (Delegate)SetAccountStatusAsync).RequireAuthorization(staff);
        endpoints.MapPost("/employee/operations/customers/{customerId:guid}/status", (Delegate)SetCustomerStatusAsync).RequireAuthorization(staff);

        return endpoints;
    }

    private static Task<IResult> SetAccountStatusAsync(
        Guid accountId, [FromForm] bool active, [FromForm] string? returnUrl, BankingOperationsApiClient operations, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        RunAsync(returnUrl, httpContext, antiforgery, (token, ct) => operations.SetAccountStatusAsync(token, accountId, active, ct), cancellationToken);

    private static Task<IResult> SetCustomerStatusAsync(
        Guid customerId, [FromForm] bool active, [FromForm] string? returnUrl, BankingOperationsApiClient operations, HttpContext httpContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        RunAsync(returnUrl, httpContext, antiforgery, (token, ct) => operations.SetCustomerStatusAsync(token, customerId, active, ct), cancellationToken);

    private static async Task<IResult> RunAsync(
        string? returnUrl,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        Func<string, CancellationToken, Task<ApiResult<NoData>>> action,
        CancellationToken cancellationToken)
    {
        // Only ever redirects back into our own operations page, never to an external URL.
        var back = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//")
            ? "/employee/operations"
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
