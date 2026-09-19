using Banking_System.ApiService.DTOs;
using Banking_System.ApiService.Models;
using Banking_System.ApiService.Repositories;
using Banking_System.ApiService.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Banking_System.ApiService.Services;

public enum RegisterStatus
{
    Registered,
    VerificationFailed,
    TooManyAttempts,
    AlreadyRegistered,
    UsernameTaken,
    EmailTaken,
    PhoneTaken
}

/// <summary>
/// Online-banking registration for people who are already bank customers.
/// The details are checked against the bank's own records and, if they match
/// and nothing is already in use, the login is created straight away.
/// </summary>
public sealed class RegistrationService
{
    private const int MaxFailedVerifications = 5;
    private static readonly TimeSpan VerificationLockout = TimeSpan.FromMinutes(30);

    private readonly IBankCustomerRepository _bankCustomers;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<RegistrationService> _logger;

    public RegistrationService(
        IBankCustomerRepository bankCustomers,
        IUserRepository users,
        IPasswordHasher<User> passwordHasher,
        ILogger<RegistrationService> logger)
    {
        _bankCustomers = bankCustomers;
        _users = users;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<RegisterStatus> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await _bankCustomers.FindByAccountOrCifAsync(
            request.AccountOrCif.Trim(),
            cancellationToken);

        // Every failure below looks the same to the caller, so nobody can use
        // this form to discover which account numbers or details exist.
        if (customer is null || !customer.IsActive)
        {
            // The caller only ever sees "could not verify"; the real reason is
            // kept in the server log (never the values typed) so staff can help.
            _logger.LogWarning(
                "Registration not verified: no active bank customer for account/CIF ending {Ending}.",
                Ending(request.AccountOrCif));

            return RegisterStatus.VerificationFailed;
        }

        if (customer.LockedUntil is not null && customer.LockedUntil > DateTime.UtcNow)
        {
            return RegisterStatus.TooManyAttempts;
        }

        var mismatch = FindMismatch(customer, request);

        if (mismatch is not null)
        {
            _logger.LogWarning(
                "Registration not verified for CIF {Cif}: {Reason}.",
                customer.CifNumber, mismatch);

            await _bankCustomers.RecordFailedVerificationAsync(
                customer.CustomerId,
                MaxFailedVerifications,
                VerificationLockout,
                cancellationToken);

            return RegisterStatus.VerificationFailed;
        }

        await _bankCustomers.ResetFailedVerificationsAsync(customer.CustomerId, cancellationToken);

        // Identity is proven from here on, so it is safe to say what is wrong.
        if (await _users.BankCustomerHasLoginAsync(customer.CustomerId, cancellationToken))
        {
            return RegisterStatus.AlreadyRegistered;
        }

        var username = UsernameRules.Normalize(request.Username);
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = ContactRules.NormalizePhone(request.PhoneNumber);

        if (UsernameRules.IsReserved(username)
            || await _users.UsernameExistsAsync(username, cancellationToken))
        {
            return RegisterStatus.UsernameTaken;
        }

        if (await _users.GetByEmailAsync(email, cancellationToken) is not null)
        {
            return RegisterStatus.EmailTaken;
        }

        if (await _users.PhoneInUseAsync(phone, cancellationToken))
        {
            return RegisterStatus.PhoneTaken;
        }

        var user = new User
        {
            UserId = Guid.NewGuid(),
            Username = username,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = email,
            PhoneNumber = phone,
            DateOfBirth = customer.DateOfBirth,
            BankCustomerId = customer.CustomerId,
            Role = "Customer",
            IsActive = true,
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
            return ex.Message.Contains("BankCustomerId", StringComparison.OrdinalIgnoreCase)
                ? RegisterStatus.AlreadyRegistered
                : ex.Message.Contains("Username", StringComparison.OrdinalIgnoreCase)
                    ? RegisterStatus.UsernameTaken
                    : ex.Message.Contains("Phone", StringComparison.OrdinalIgnoreCase)
                        ? RegisterStatus.PhoneTaken
                        : RegisterStatus.EmailTaken;
        }

        return RegisterStatus.Registered;
    }

    private static string Ending(string value)
    {
        var trimmed = value.Trim();

        return trimmed.Length <= 3 ? "***" : "..." + trimmed[^3..];
    }

    // Name, phone and at least one of date of birth / national ID must all
    // agree with the bank's record; anything the customer supplied must match.
    // Returns why they do not (for the server log), or null when they all match.
    private static string? FindMismatch(BankCustomer customer, RegisterRequest request)
    {
        var hasDob = request.DateOfBirth is not null;
        var hasNationalId = !string.IsNullOrWhiteSpace(request.NationalId);

        if (!hasDob && !hasNationalId)
        {
            return "neither date of birth nor national ID was given";
        }

        if (!ContactRules.NamesMatch(customer.FirstName, request.FirstName))
        {
            return "first name does not match the bank record";
        }

        if (!ContactRules.NamesMatch(customer.LastName, request.LastName))
        {
            return "last name does not match the bank record";
        }

        if (!ContactRules.PhonesMatch(customer.PhoneNumber, request.PhoneNumber))
        {
            return "phone number does not match the bank record";
        }

        if (hasDob && customer.DateOfBirth != request.DateOfBirth)
        {
            return "date of birth does not match the bank record";
        }

        if (hasNationalId
            && (string.IsNullOrWhiteSpace(customer.NationalId)
                || ContactRules.NormalizeNationalId(customer.NationalId)
                    != ContactRules.NormalizeNationalId(request.NationalId!)))
        {
            return "national ID does not match the bank record (or none is on file)";
        }

        return null;
    }
}
