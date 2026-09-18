using System.Net;
using System.Net.Http.Json;
using Banking_System.Web.Models.Authentication;

namespace Banking_System.Web.Services.Authentication;

public enum RegisterResult
{
    Success,
    AlreadyExists,
    InvalidRequest
}

public sealed class AuthApiClient
{
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/auth/login",
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        if ((int)response.StatusCode >= 500)
        {
            response.EnsureSuccessStatusCode();
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(
            cancellationToken: cancellationToken);
    }

    public async Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/auth/register",
            request,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return RegisterResult.Success;
        }

        if ((int)response.StatusCode >= 500)
        {
            response.EnsureSuccessStatusCode();
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Conflict => RegisterResult.AlreadyExists,
            HttpStatusCode.BadRequest => RegisterResult.InvalidRequest,
            _ => RegisterResult.InvalidRequest
        };
    }
}
