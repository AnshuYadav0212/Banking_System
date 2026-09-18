using System.Security.Claims;
using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Services;
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

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _authService.RegisterAsync(
                request,
                cancellationToken);

            return StatusCode(StatusCodes.Status201Created);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", System.StringComparison.OrdinalIgnoreCase))
        {
           return Conflict(new
            {
                error = "UserAlreadyExists"
            });
        }
        catch (Exception)
        {         return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
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
                message = "Invalid email or password."
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
