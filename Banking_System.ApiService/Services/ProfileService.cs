using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;
using Microsoft.AspNetCore.Identity;

namespace Banking_System.ApiService.Services;

public enum ChangePasswordStatus
{
    Success,
    NotAllowed,
    IncorrectCurrentPassword
}

public sealed record ChangePasswordResult(ChangePasswordStatus Status);

/// <summary>The signed-in customer's own profile: read-only personal/contact details, and changing their password.</summary>
public sealed class ProfileService
{
    private readonly IUserRepository _users;
    private readonly IBankCustomerRepository _customers;
    private readonly IPasswordHasher<User> _passwordHasher;

    public ProfileService(
        IUserRepository users,
        IBankCustomerRepository customers,
        IPasswordHasher<User> passwordHasher)
    {
        _users = users;
        _customers = customers;
        _passwordHasher = passwordHasher;
    }

    public async Task<MyProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);

        if (user is not { BankCustomerId: { } customerId })
        {
            return null;
        }

        var customer = await _customers.GetByIdAsync(customerId, cancellationToken);

        if (customer is null)
        {
            return null;
        }

        return new MyProfileResponse(
            user.Username ?? string.Empty,
            user.Email,
            customer.FirstName,
            customer.LastName,
            customer.DateOfBirth,
            customer.PhoneNumber,
            user.IsActive);
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);

        if (user is not { IsActive: true })
        {
            return new ChangePasswordResult(ChangePasswordStatus.NotAllowed);
        }

        var verified = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);

        if (verified == PasswordVerificationResult.Failed)
        {
            return new ChangePasswordResult(ChangePasswordStatus.IncorrectCurrentPassword);
        }

        await _users.UpdatePasswordHashAsync(
            user.UserId,
            _passwordHasher.HashPassword(user, request.NewPassword),
            cancellationToken);

        return new ChangePasswordResult(ChangePasswordStatus.Success);
    }
}
