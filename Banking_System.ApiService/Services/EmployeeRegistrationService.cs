using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;
using Banking_System.ApiService.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Banking_System.ApiService.Services;

public enum EmployeeRegisterStatus
{
    Registered,
    UsernameTaken,
    EmailTaken
}

public sealed record EmployeeRegisterResult(EmployeeRegisterStatus Status)
{
    public static implicit operator EmployeeRegisterResult(EmployeeRegisterStatus status) => new(status);
}

/// <summary>
/// Self-service registration for staff logins. Unlike <see cref="RegistrationService"/>,
/// this is not linked to a bank customer and is not checked against any bank record -
/// the staff email domain (enforced on <see cref="StaffRegisterRequest"/>) is the only gate.
/// </summary>
public sealed class EmployeeRegistrationService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<User> _passwordHasher;

    public EmployeeRegistrationService(
        IUserRepository users,
        IPasswordHasher<User> passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    public async Task<EmployeeRegisterResult> RegisterAsync(
        StaffRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var username = UsernameRules.Normalize(request.Username);
        var email = request.Email.Trim().ToLowerInvariant();

        if (UsernameRules.IsReserved(username)
            || await _users.UsernameExistsAsync(username, cancellationToken))
        {
            return EmployeeRegisterStatus.UsernameTaken;
        }

        if (await _users.GetByEmailAsync(email, cancellationToken) is not null)
        {
            return EmployeeRegisterStatus.EmailTaken;
        }

        var user = new User
        {
            UserId = Guid.NewGuid(),
            Username = username,
            Email = email,
            BankCustomerId = null,
            RoleId = RoleIds.Employee,
            StatusId = StatusIds.Active,
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        try
        {
            await _users.CreateAsync(user, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // The unique indexes are the final guard against two people
            // registering the same details at the same moment.
            return ex.Message.Contains("UX_Users_Username", StringComparison.OrdinalIgnoreCase)
                ? EmployeeRegisterStatus.UsernameTaken
                : EmployeeRegisterStatus.EmailTaken;
        }

        return EmployeeRegisterStatus.Registered;
    }
}
