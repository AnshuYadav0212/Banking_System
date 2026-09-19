using Banking_System.ApiService.Models;

namespace Banking_System.ApiService.Repositories;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>True if any login (active or not) already uses this phone number.</summary>
    Task<bool> PhoneInUseAsync(string phone, CancellationToken cancellationToken = default);

    Task<bool> BankCustomerHasLoginAsync(Guid bankCustomerId, CancellationToken cancellationToken = default);

    Task UpdatePasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default);

    Task CreateAsync(User user, CancellationToken cancellationToken = default);
}
