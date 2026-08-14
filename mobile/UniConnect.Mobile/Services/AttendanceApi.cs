using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Typed client over /api/attendance.
///
/// Same shape as StudyGroupsApi/NotificationsApi: read the body once, parse
/// from the string, and turn a failed status into an ApiException carrying the
/// server's own wording so the page can show it without knowing HTTP.
/// </summary>
public class AttendanceApi
{
    private readonly HttpClient _http;

    public AttendanceApi(HttpClient http) => _http = http;

    // ---- reads --------------------------------------------------------------

    /// <summary>Everything the signed-in student has ever checked into, newest first.</summary>
    public async Task<List<AttendanceHistoryEntry>> GetHistoryAsync(CancellationToken ct = default) =>
        await GetAsync<List<AttendanceHistoryEntry>>("api/attendance/history", ct) ?? new List<AttendanceHistoryEntry>();

    /// <summary>
    /// Looks a token up without submitting anything — the confirmation step
    /// before GPS is even requested, so a bad or expired code fails fast.
    /// </summary>
    public async Task<SessionInfoDto> GetSessionInfoAsync(string token, CancellationToken ct = default) =>
        await GetAsync<SessionInfoDto>($"api/attendance/session-info?token={Uri.EscapeDataString(token)}", ct)
        ?? throw Empty("session");

    // ---- writes ---------------------------------------------------------------

    public async Task<AttendanceSubmitResponse> SubmitAsync(
        string token, double? lat, double? lng, CancellationToken ct = default)
    {
        var request = new AttendanceSubmitRequest { Token = token, Lat = lat, Lng = lng };
        var (status, body) = await SendAsync(
            () => _http.PostAsJsonAsync("api/attendance/submit", request, ApiJson.Options, ct), ct);

        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<AttendanceSubmitResponse>(body) ?? throw Empty("result");
    }

    // ---- plumbing (identical to the sibling API clients) -----------------------

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
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

    private static ApiException Empty(string what) =>
        new(HttpStatusCode.InternalServerError, $"The server did not return a {what}.", "EMPTY_RESPONSE");
}
