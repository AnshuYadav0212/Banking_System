using Banking_System.ApiService.Models;

namespace Banking_System.ApiService.Services;

public interface IJwtTokenService
{
    string GenerateToken(User user);
}
