using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banking_System.Web.Models.Operations;
using Banking_System.Web.Services.Authentication;

namespace Banking_System.Web.Services.Operations;

/// <summary>Calls the staff banking-operations API: freeze/unfreeze an account, restrict/restore a customer.</summary>
public sealed class BankingOperationsApiClient
{
    private readonly HttpClient _httpClient;

    public BankingOperationsApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ApiResult<List<AccountOperationRow>>> SearchAccountsAsync(
        string accessToken, string? search, CancellationToken cancellationToken = default) =>
        SendAsync<List<AccountOperationRow>>(
            HttpMethod.Get, "/api/operations/accounts?q=" + Uri.EscapeDataString(search ?? ""), null, accessToken, cancellationToken);

    public Task<ApiResult<NoData>> SetAccountStatusAsync(
        string accessToken, Guid accountId, bool active, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/operations/accounts/{accountId}/status", new { Active = active }, accessToken, cancellationToken);

    public Task<ApiResult<List<CustomerOperationRow>>> SearchCustomersAsync(
        string accessToken, string? search, CancellationToken cancellationToken = default) =>
        SendAsync<List<CustomerOperationRow>>(
            HttpMethod.Get, "/api/operations/customers?q=" + Uri.EscapeDataString(search ?? ""), null, accessToken, cancellationToken);

    public Task<ApiResult<NoData>> SetCustomerStatusAsync(
        string accessToken, Guid customerId, bool active, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/operations/customers/{customerId}/status", new { Active = active }, accessToken, cancellationToken);

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

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ApiResult<T>(false, Error: "NotFound");
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

        return new ApiResult<T>(false, Error: "Invalid");
    }
}
