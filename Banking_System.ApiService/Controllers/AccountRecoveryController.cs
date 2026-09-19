using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.ApiService.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/account-recovery")]
public sealed class AccountRecoveryController : ControllerBase
{
    private readonly AccountRecoveryService _recovery;

    public AccountRecoveryController(AccountRecoveryService recovery)
    {
        _recovery = recovery;
    }

    // Always the same answer, whether or not an account matched.
    [HttpPost("forgot-username")]
    public async Task<IActionResult> ForgotUsername(
        ForgotUsernameRequest request,
        CancellationToken cancellationToken)
    {
        await _recovery.RecoverUsernameAsync(request.Contact, cancellationToken);

        return Accepted();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _recovery.StartPasswordResetAsync(request.Identifier, cancellationToken));
    }

    [HttpPost("validate-reset-link")]
    public async Task<IActionResult> ValidateResetLink(
        ValidateResetLinkRequest request,
        CancellationToken cancellationToken)
    {
        return await _recovery.IsResetLinkValidAsync(request.ResetId, request.Token, cancellationToken)
            ? Ok(new { valid = true })
            : StatusCode(StatusCodes.Status410Gone, new { error = "Expired" });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _recovery.ResetPasswordAsync(request, cancellationToken);

        return result.Status switch
        {
            ResetPasswordStatus.Success => Ok(new { reset = true }),
            ResetPasswordStatus.TooManyAttempts =>
                StatusCode(StatusCodes.Status429TooManyRequests, new { error = "TooManyAttempts" }),
            _ => StatusCode(StatusCodes.Status410Gone, new { error = "Expired" })
        };
    }
}
