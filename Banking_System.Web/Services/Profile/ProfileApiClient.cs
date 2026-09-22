using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banking_System.Web.Models.Profile;
using Banking_System.Web.Services.Authentication;

namespace Banking_System.Web.Services.Profile;

/// <summary>Calls the profile API on behalf of the signed-in customer.</summary>
public sealed class ProfileApiClient
{
    private readonly HttpClient _httpClient;

    public ProfileApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ApiResult<MyProfile>> GetProfileAsync(
        string accessToken, CancellationToken cancellationToken = default) =>
        SendAsync<MyProfile>(HttpMethod.Get, "/api/profile", null, accessToken, cancellationToken);

    public Task<ApiResult<NoData>> ChangePasswordAsync(
        string accessToken, ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, "/api/profile/change-password", request, accessToken, cancellationToken);

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method, string path, object? body, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode >= 500)
        {
            response.EnsureSuccessStatusCode();
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ApiResult<T>(false, Error: "Unauthorized");
        }

        if (response.IsSuccessStatusCode)
        {
            if (typeof(T) == typeof(NoData))
            {
                return new ApiResult<T>(true, default);
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

        return new ApiResult<T>(false, Error: error?.Error ?? "Invalid", Hint: error?.Hint);
    }

    private sealed record ErrorBody(string? Error, string? Hint);
}
