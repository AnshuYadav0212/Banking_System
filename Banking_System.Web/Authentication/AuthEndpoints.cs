using System.Security.Claims;
using Banking_System.Web.Models.Authentication;
using Banking_System.Web.Services.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.Web.Authentication;

public static class BankingTokens
{
    /// <summary>Name under which the API access token is kept in the sign-in cookie.</summary>
    public const string AccessToken = "access_token";
}

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {

        endpoints.MapPost("/auth/login", (Delegate)LoginAsync);

        endpoints.MapGet("/auth/username-available", (Delegate)UsernameAvailableAsync)
            .RequireRateLimiting("username-check");

        endpoints.MapPost("/auth/logout", (Delegate)LogoutAsync);
           
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
         [FromForm] LoginRequest request,
         AuthApiClient authApiClient,
         HttpContext httpContext,
         IAntiforgery antiforgery,
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

        LoginResponse? result;

        try
        {
            result = await authApiClient.LoginAsync(
                request,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Results.Redirect("/login?error=unavailable");
        }

        if (result is null)
        {
            return Results.Redirect("/login?error=invalid");
        }

        var claims = new List<Claim>
        {
            new(
                ClaimTypes.NameIdentifier,
                result.UserId),

            new(
                ClaimTypes.Name,
                result.Email),

            new(
                ClaimTypes.Email,
                result.Email),

            new(
                ClaimTypes.Role,
                result.Role)
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc =
                DateTimeOffset.UtcNow.AddHours(1),
            AllowRefresh = true
        };

        // Keep the API's access token with the sign-in, so pages that act on the
        // customer's behalf (such as sending money) can call the API as them.
        properties.StoreTokens(
        [
            new AuthenticationToken
            {
                Name = BankingTokens.AccessToken,
                Value = result.AccessToken
            }
        ]);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);

        return result.Role switch
        {
            "Customer" => Results.Redirect("/customer"),
            "Employee" => Results.Redirect("/employee"),
            "Admin" => Results.Redirect("/admin/users"),
            _ => Results.Redirect("/access-denied")
        };
    }

    // Registration itself is handled by the /register page (an EditForm), so
    // the user keeps what they typed when the server reports a problem.
    private static async Task<IResult> UsernameAvailableAsync(
        string? username,
        AuthApiClient authApiClient,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";

        try
        {
            var result = await authApiClient.CheckUsernameAsync(
                username ?? string.Empty,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(
                "Invalid antiforgery token.");
        }

        await httpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.Redirect("/login");
    }
}