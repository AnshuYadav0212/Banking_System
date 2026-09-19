using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking_System.ApiService.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/registration")]
public sealed class RegistrationController : ControllerBase
{
    private readonly RegistrationService _registration;

    public RegistrationController(RegistrationService registration)
    {
        _registration = registration;
    }

    [HttpPost]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var status = await _registration.RegisterAsync(request, cancellationToken);

        return status switch
        {
            RegisterStatus.Registered => StatusCode(StatusCodes.Status201Created),
            RegisterStatus.VerificationFailed =>
                UnprocessableEntity(new { error = "VerificationFailed" }),
            RegisterStatus.TooManyAttempts =>
                StatusCode(StatusCodes.Status429TooManyRequests, new { error = "TooManyAttempts" }),
            RegisterStatus.AlreadyRegistered =>
                Conflict(new { error = "AlreadyRegistered" }),
            RegisterStatus.UsernameTaken =>
                Conflict(new { error = "UsernameTaken" }),
            RegisterStatus.EmailTaken =>
                Conflict(new { error = "EmailTaken" }),
            RegisterStatus.PhoneTaken =>
                Conflict(new { error = "PhoneTaken" }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
