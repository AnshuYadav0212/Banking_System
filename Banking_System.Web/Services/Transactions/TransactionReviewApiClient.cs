using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banking_System.Web.Models.Transactions;
using Banking_System.Web.Services.Authentication;

namespace Banking_System.Web.Services.Transactions;

/// <summary>Calls the employee transaction-review API on behalf of the signed-in employee or admin.</summary>
public sealed class TransactionReviewApiClient
{
    private readonly HttpClient _httpClient;

    public TransactionReviewApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ApiResult<TransactionQueuePage>> SearchQueueAsync(
        string accessToken, string? search, string? status, DateOnly? from, DateOnly? to, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };

        if (!string.IsNullOrWhiteSpace(search)) query.Add("q=" + Uri.EscapeDataString(search.Trim()));
        if (status is not null) query.Add("status=" + Uri.EscapeDataString(status));
        if (from is { } f) query.Add("from=" + f.ToString("yyyy-MM-dd"));
        if (to is { } t) query.Add("to=" + t.ToString("yyyy-MM-dd"));

        return SendAsync<TransactionQueuePage>(
            HttpMethod.Get, "/api/transactions/queue?" + string.Join('&', query), null, accessToken, cancellationToken);
    }

    public Task<ApiResult<TransactionReviewDetail>> GetDetailAsync(
        string accessToken, Guid transactionId, CancellationToken cancellationToken = default) =>
        SendAsync<TransactionReviewDetail>(HttpMethod.Get, $"/api/transactions/{transactionId}", null, accessToken, cancellationToken);

    public Task<ApiResult<NoData>> StartReviewAsync(
        string accessToken, Guid transactionId, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/transactions/{transactionId}/start-review", null, accessToken, cancellationToken);

    public Task<ApiResult<NoData>> ApproveAsync(
        string accessToken, Guid transactionId, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/transactions/{transactionId}/approve", null, accessToken, cancellationToken);

    public Task<ApiResult<NoData>> RejectAsync(
        string accessToken, Guid transactionId, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/transactions/{transactionId}/reject", null, accessToken, cancellationToken);

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
            // Not our error shape.
        }

        return new ApiResult<T>(false, Error: error?.Error ?? "Invalid");
    }

    private sealed record ErrorBody(string? Error);
}
