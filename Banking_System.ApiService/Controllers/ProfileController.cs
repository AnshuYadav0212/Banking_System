using System.Security.Claims;
using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.ApiService.Controllers;

/// <summary>The signed-in customer's own profile.</summary>
[ApiController]
[Authorize(Policy = "CustomerOnly")]
[Route("api/profile")]
public sealed class ProfileController : ControllerBase
{
    private readonly ProfileService _profile;

    public ProfileController(ProfileService profile)
    {
        _profile = profile;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var profile = await _profile.GetProfileAsync(userId, cancellationToken);

        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _profile.ChangePasswordAsync(userId, request, cancellationToken);

        return result.Status switch
        {
            ChangePasswordStatus.Success => Ok(),
            ChangePasswordStatus.IncorrectCurrentPassword =>
                UnprocessableEntity(new { error = "IncorrectCurrentPassword" }),
            ChangePasswordStatus.NotAllowed => Forbid(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
