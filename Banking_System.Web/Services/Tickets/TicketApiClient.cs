using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banking_System.Web.Models.Tickets;
using Banking_System.Web.Services.Authentication;

namespace Banking_System.Web.Services.Tickets;

/// <summary>Calls the support-ticket API on behalf of the signed-in user (customer or staff).</summary>
public sealed class TicketApiClient
{
    private readonly HttpClient _httpClient;

    public TicketApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ApiResult<CreateTicketResponse>> CreateAsync(
        string accessToken, CreateTicketRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreateTicketResponse>(HttpMethod.Post, "/api/tickets", request, accessToken, cancellationToken);

    public Task<ApiResult<List<TicketSummary>>> GetMineAsync(
        string accessToken, Guid? status = null, CancellationToken cancellationToken = default) =>
        SendAsync<List<TicketSummary>>(
            HttpMethod.Get, "/api/tickets/mine" + StatusQuery(status), null, accessToken, cancellationToken);

    public Task<ApiResult<List<TicketSummary>>> GetQueueAsync(
        string accessToken, Guid? status = null, Guid? category = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();

        if (status is { } s) query.Add($"status={s}");
        if (category is { } c) query.Add($"category={c}");

        var path = "/api/tickets/queue" + (query.Count > 0 ? "?" + string.Join('&', query) : "");

        return SendAsync<List<TicketSummary>>(HttpMethod.Get, path, null, accessToken, cancellationToken);
    }

    public Task<ApiResult<TicketDetail>> GetDetailAsync(
        string accessToken, Guid ticketId, CancellationToken cancellationToken = default) =>
        SendAsync<TicketDetail>(HttpMethod.Get, $"/api/tickets/{ticketId}", null, accessToken, cancellationToken);

    public Task<ApiResult<NoData>> StartReviewAsync(
        string accessToken, Guid ticketId, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/tickets/{ticketId}/start-review", null, accessToken, cancellationToken);

    public Task<ApiResult<NoData>> ResolveAsync(
        string accessToken, Guid ticketId, string note, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/tickets/{ticketId}/resolve", new TicketActionRequest(note), accessToken, cancellationToken);

    public Task<ApiResult<NoData>> RejectAsync(
        string accessToken, Guid ticketId, string note, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/tickets/{ticketId}/reject", new TicketActionRequest(note), accessToken, cancellationToken);

    public Task<ApiResult<NoData>> CloseAsync(
        string accessToken, Guid ticketId, string? note, CancellationToken cancellationToken = default) =>
        SendAsync<NoData>(HttpMethod.Post, $"/api/tickets/{ticketId}/close", new TicketActionRequest(note), accessToken, cancellationToken);

    private static string StatusQuery(Guid? status) => status is { } s ? $"?status={s}" : "";

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

public sealed record CreateTicketResponse(Guid TicketId);
