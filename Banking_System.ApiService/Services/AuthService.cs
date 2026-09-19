using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;
using Banking_System.ApiService.Validation;
using Microsoft.AspNetCore.Identity;

namespace Banking_System.ApiService.Services;

public sealed class AuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher<User> passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<bool> IsUsernameAvailableAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var normalized = UsernameRules.Normalize(username);

        if (!UsernameRules.IsValidFormat(normalized) || UsernameRules.IsReserved(normalized))
        {
            return false;
        }

        return !await _userRepository.UsernameExistsAsync(normalized, cancellationToken);
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var identifier = request.Identifier.Trim();

        // Usernames can't contain '@', so the identifier unambiguously
        // selects which unique index to seek.
        var user = identifier.Contains('@')
            ? await _userRepository.GetByEmailAsync(
                identifier.ToLowerInvariant(),
                cancellationToken)
            : await _userRepository.GetByUsernameAsync(
                UsernameRules.Normalize(identifier),
                cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException(
                "Invalid credentials.");
        }

        var result = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            throw new UnauthorizedAccessException(
                "Invalid credentials.");
        }

        var accessToken = _jwtTokenService.GenerateToken(user);

        return new LoginResponse(
            accessToken,
            user.UserId.ToString(),
            user.Email,
            user.Role);
    }
}
