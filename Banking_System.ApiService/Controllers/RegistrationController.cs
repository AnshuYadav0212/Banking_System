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
    private readonly IHostEnvironment _environment;

    public RegistrationController(RegistrationService registration, IHostEnvironment environment)
    {
        _registration = registration;
        _environment = environment;
    }

    [HttpPost]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _registration.RegisterAsync(request, cancellationToken);

        return result.Status switch
        {
            RegisterStatus.Registered => StatusCode(StatusCodes.Status201Created),
            RegisterStatus.VerificationFailed =>
                // The customer only ever sees a generic message. In Development the
                // reason is included too, so a failing registration can be diagnosed.
                UnprocessableEntity(new
                {
                    error = "VerificationFailed",
                    hint = _environment.IsDevelopment() ? result.Reason : null
                }),
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
