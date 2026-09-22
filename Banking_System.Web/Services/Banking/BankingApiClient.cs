using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banking_System.Web.Models.Banking;
using Banking_System.Web.Services.Authentication;

namespace Banking_System.Web.Services.Banking;

/// <summary>
/// Calls the banking API on behalf of the signed-in customer, presenting the
/// access token that was kept with their sign-in.
/// </summary>
public sealed class BankingApiClient
{
    private readonly HttpClient _httpClient;

    public BankingApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ApiResult<List<AccountSummary>>> GetMyAccountsAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendAsync<List<AccountSummary>>(HttpMethod.Get, "/api/accounts/mine", null, accessToken, cancellationToken);

    public Task<ApiResult<List<TransactionSummary>>> GetHistoryAsync(
        string accessToken,
        int? take = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<List<TransactionSummary>>(
            HttpMethod.Get,
            take is { } n ? $"/api/transfers?take={n}" : "/api/transfers",
            null,
            accessToken,
            cancellationToken);

    public Task<ApiResult<TransferResponse>> TransferAsync(
        string accessToken,
        TransferRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<TransferResponse>(HttpMethod.Post, "/api/transfers", request, accessToken, cancellationToken);

    public Task<ApiResult<RecipientVerification>> VerifyRecipientAsync(
        string accessToken,
        string accountNumber,
        CancellationToken cancellationToken = default) =>
        SendAsync<RecipientVerification>(
            HttpMethod.Get,
            $"/api/accounts/verify/{Uri.EscapeDataString(accountNumber)}",
            null,
            accessToken,
            cancellationToken);

    // A 5xx or network failure throws HttpRequestException (the page reports
    // "service unavailable"). A rejected or expired token becomes the error
    // "Unauthorized". Anything else the API refuses becomes its error code.
    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string accessToken,
        CancellationToken cancellationToken)
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
