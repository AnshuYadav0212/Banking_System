using System.Security.Claims;
using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Services;
using Banking_System.ApiService.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.ApiService.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    // Registration lives in RegistrationController: online accounts can only be
    // created after verifying against the bank's customer records.

    [AllowAnonymous]
    [HttpGet("username-available")]
    public async Task<IActionResult> UsernameAvailable(
        [FromQuery] string? username,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!UsernameRules.IsValidFormat(username))
        {
            return Ok(new { available = false, reason = "invalid" });
        }

        var available = await _authService.IsUsernameAvailableAsync(
            username!,
            cancellationToken);

        return Ok(new { available, reason = available ? null : "taken" });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authService.LoginAsync(
                request,
                cancellationToken);

            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new
            {
                message = "Invalid username/email or password."
            });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = User.FindFirstValue(ClaimTypes.Email),
            Role = User.FindFirstValue(ClaimTypes.Role)
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin-test")]
    public IActionResult AdminTest()
    {
        return Ok(new
        {
            message = "You are authorized as an Admin."
        });
    }


    [Authorize(Roles = "Customer")]
    [HttpGet("customer-test")]
    public IActionResult CustomerTest()
    {
        return Ok(new
        {
            message = "You are authorized as a Customer."
        });
    }
}
