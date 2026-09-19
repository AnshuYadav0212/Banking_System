using System.Net;
using System.Net.Http.Json;
using Banking_System.Web.Models.Authentication;

namespace Banking_System.Web.Services.Authentication;

/// <summary>Outcome of an API call that can fail for reasons the UI wants to explain.</summary>
public sealed record ApiResult<T>(
    bool Success,
    T? Data = default,
    string? Error = null,
    int AttemptsLeft = 0);

/// <summary>Placeholder for calls that return no body.</summary>
public sealed record NoData;

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

    public async Task<UsernameAvailability?> CheckUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            "/api/auth/username-available?username=" + Uri.EscapeDataString(username),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UsernameAvailability>(
            cancellationToken: cancellationToken);
    }

    // Registration -------------------------------------------------------

    /// <summary>Creates the online-banking login if the details match the bank's records.</summary>
    public Task<ApiResult<NoData>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<NoData>("/api/registration", request, cancellationToken);

    // Account recovery ---------------------------------------------------

    public Task<ApiResult<NoData>> ForgotUsernameAsync(
        string contact,
        CancellationToken cancellationToken = default) =>
        PostAsync<NoData>("/api/account-recovery/forgot-username", new { contact }, cancellationToken);

    public Task<ApiResult<ForgotPasswordResponse>> ForgotPasswordAsync(
        string identifier,
        CancellationToken cancellationToken = default) =>
        PostAsync<ForgotPasswordResponse>("/api/account-recovery/forgot-password", new { identifier }, cancellationToken);

    /// <summary>True while the emailed link is unused and unexpired.</summary>
    public async Task<bool> IsResetLinkValidAsync(
        Guid resetId,
        string token,
        CancellationToken cancellationToken = default) =>
        (await PostAsync<NoData>("/api/account-recovery/validate-reset-link", new { resetId, token }, cancellationToken)).Success;

    public Task<ApiResult<NoData>> ResetPasswordAsync(
        Guid resetId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        PostAsync<NoData>("/api/account-recovery/reset-password", new { resetId, token, newPassword }, cancellationToken);

    // A 5xx or network failure throws HttpRequestException, which the pages
    // report as "service unavailable". Anything else becomes an ApiResult.
    private async Task<ApiResult<T>> PostAsync<T>(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, cancellationToken);

        if ((int)response.StatusCode >= 500)
        {
            response.EnsureSuccessStatusCode();
        }

        if (response.IsSuccessStatusCode)
        {
            if (typeof(T) == typeof(NoData))
            {
                return new ApiResult<T>(true);
            }

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);

            return new ApiResult<T>(true, data);
        }

        ErrorBody? error = null;

        try
        {
            error = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // Not our error shape (e.g. model-validation problem details).
        }

        return new ApiResult<T>(
            false,
            Error: error?.Error ?? "Invalid",
            AttemptsLeft: error?.AttemptsLeft ?? 0);
    }

    private sealed record ErrorBody(string? Error, int? AttemptsLeft);
}
