using System.Net;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Typed client over /api/notifications.
///
/// Note the asymmetry between the two calls, which is deliberate on the server:
/// fetching the list marks everything in it read, while the count does not. Use
/// <see cref="GetUnreadCountAsync"/> for the badge, or checking it would clear
/// the very thing it is reporting.
/// </summary>
public class NotificationsApi
{
    private readonly HttpClient _http;

    public NotificationsApi(HttpClient http) => _http = http;

    /// <summary>
    /// The 100 most recent notifications, newest first. Marks them read as a
    /// side effect — each item's WasUnread records what it was on arrival.
    /// </summary>
    public async Task<List<NotificationDto>> GetAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NotificationDto>>("api/notifications", ct) ?? new List<NotificationDto>();

    /// <summary>
    /// How many are unread, without marking anything. Failures return 0 rather
    /// than throwing: this only drives a badge, and a broken badge must not
    /// break the screen it sits on.
    /// </summary>
    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await GetJsonAsync<UnreadCountDto>("api/notifications/unread-count", ct);
            return result?.Count ?? 0;
        }
        catch (ApiException)
        {
            return 0;
        }
    }

    // ---- plumbing ---------------------------------------------------------

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        var (status, body) = await SendAsync(() => _http.GetAsync(url, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<T>(body);
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            using var response = await send();
            return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(
                HttpStatusCode.ServiceUnavailable,
                $"Could not reach the server at {ApiConfig.BaseAddress}. {ex.GetBaseException().Message}",
                "NO_CONNECTION");
        }
        catch (TaskCanceledException)
        {
            throw new ApiException(HttpStatusCode.RequestTimeout, "The server took too long to respond.", "TIMEOUT");
        }
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static T? Parse<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(body, ApiJson.Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static ApiException ToException(HttpStatusCode status, string body)
    {
        var error = Parse<ApiError>(body);

        var message = error?.Error ?? status switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            _ => $"The server returned {(int)status}."
        };

        return new ApiException(status, message, error?.Code);
    }
}
