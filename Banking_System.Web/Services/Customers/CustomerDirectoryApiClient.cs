using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banking_System.Web.Models.Customers;
using Banking_System.Web.Services.Authentication;

namespace Banking_System.Web.Services.Customers;

/// <summary>Calls the staff customer-directory API on behalf of the signed-in employee or admin.</summary>
public sealed class CustomerDirectoryApiClient
{
    private readonly HttpClient _httpClient;

    public CustomerDirectoryApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ApiResult<CustomerAccountPage>> SearchAsync(
        string accessToken,
        string? search,
        string? status,
        bool? onlineBanking,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };

        if (!string.IsNullOrWhiteSpace(search)) query.Add("q=" + Uri.EscapeDataString(search.Trim()));
        if (status is not null) query.Add("status=" + Uri.EscapeDataString(status));
        if (onlineBanking is { } ob) query.Add("onlineBanking=" + (ob ? "true" : "false"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers?" + string.Join('&', query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode >= 500)
        {
            response.EnsureSuccessStatusCode();
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ApiResult<CustomerAccountPage>(false, Error: "Unauthorized");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new ApiResult<CustomerAccountPage>(false, Error: "Invalid");
        }

        var data = await response.Content.ReadFromJsonAsync<CustomerAccountPage>(cancellationToken: cancellationToken);

        return new ApiResult<CustomerAccountPage>(true, data);
    }
}
