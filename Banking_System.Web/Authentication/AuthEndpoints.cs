using System.Security.Claims;
using Banking_System.Web.Models.Authentication;
using Banking_System.Web.Services.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.Web.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {

        endpoints.MapPost("/auth/login", (Delegate)LoginAsync);

        endpoints.MapPost("/auth/register", (Delegate)RegisterAsync);

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

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc =
                    DateTimeOffset.UtcNow.AddHours(1),
                AllowRefresh = true
            });

        return result.Role switch
        {
            "Customer" => Results.Redirect("/customer"),
            "Employee" => Results.Redirect("/employee"),
            "Admin" => Results.Redirect("/admin/users"),
            _ => Results.Redirect("/access-denied")
        };
    }

    private static async Task<IResult> RegisterAsync(
    [FromForm] RegisterForm request,
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

        if (!string.Equals(
                request.Password,
                request.ConfirmPassword,
                StringComparison.Ordinal))
        {
            return Results.Redirect(
                "/register?error=confirm");
        }

        RegisterResult result;

        try
        {
            result = await authApiClient.RegisterAsync(
                new RegisterRequest(
                    request.Email,
                    request.Password),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Results.Redirect(
                "/register?error=unavailable");
        }

        return result switch
        {
            RegisterResult.Success =>
                Results.Redirect("/login?registered=true"),

            RegisterResult.AlreadyExists =>
                Results.Redirect("/register?error=exists"),

            RegisterResult.InvalidRequest =>
                Results.Redirect("/register?error=invalid"),

            _ =>
                Results.Redirect("/register?error=invalid")
        };
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

    public sealed record RegisterForm(
        string Email,
        string Password,
        string ConfirmPassword);
}