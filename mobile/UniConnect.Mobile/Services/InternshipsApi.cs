using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Typed client over /api/internships — browse, view, apply, track, withdraw.
///
/// Shares the response-handling shape of <see cref="StudyGroupsApi"/>: each body
/// is read to a string once and parsed from there, because reading an
/// HttpContent twice yields an empty stream the second time and turns a real
/// error into a mysterious null.
/// </summary>
public class InternshipsApi
{
    private readonly HttpClient _http;

    public InternshipsApi(HttpClient http) => _http = http;

    // ---- reads ------------------------------------------------------------

    /// <summary>
    /// Live listings, best match first. Every filter is optional and applied by
    /// the server, which is also what decides visibility by university.
    /// </summary>
    public async Task<List<InternshipSummary>> GetInternshipsAsync(
        string? skill = null,
        string? location = null,
        int? maxDuration = null,
        bool myMajorOnly = false,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(skill)) query.Add($"skill={Uri.EscapeDataString(skill)}");
        if (!string.IsNullOrWhiteSpace(location)) query.Add($"location={Uri.EscapeDataString(location)}");
        if (maxDuration is not null) query.Add($"maxDuration={maxDuration}");
        if (myMajorOnly) query.Add("myMajorOnly=true");

        var url = query.Count == 0 ? "api/internships" : $"api/internships?{string.Join("&", query)}";
        return await GetAsync<List<InternshipSummary>>(url, ct) ?? new List<InternshipSummary>();
    }

    public async Task<InternshipDetail> GetInternshipAsync(int id, CancellationToken ct = default) =>
        await GetAsync<InternshipDetail>($"api/internships/{id}", ct) ?? throw Empty("internship");

    public async Task<List<ApplicationSummary>> GetMyApplicationsAsync(CancellationToken ct = default) =>
        await GetAsync<List<ApplicationSummary>>("api/internships/applications", ct)
        ?? new List<ApplicationSummary>();

    // ---- writes -----------------------------------------------------------

    /// <summary>
    /// Applies. The refusals the server can return — deadline passed, already
    /// applied, positions filled, external listing — all arrive as an
    /// <see cref="ApiException"/> carrying the server's own wording.
    /// </summary>
    public async Task<string> ApplyAsync(int id, string? coverMessage, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            () => _http.PostAsJsonAsync($"api/internships/{id}/apply", new { coverMessage }, ApiJson.Options, ct), ct);

        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<ActionResponse>(body)?.Message ?? "Application submitted.";
    }

    public async Task<string> WithdrawAsync(int applicationId, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            () => _http.PostAsync($"api/internships/applications/{applicationId}/withdraw", null, ct), ct);

        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<ActionResponse>(body)?.Message ?? "Application withdrawn.";
    }

    // ---- plumbing ---------------------------------------------------------

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

    private static ApiException Empty(string what) =>
        new(HttpStatusCode.NoContent, $"The server returned an empty {what}.");

    /// <summary>
    /// The endpoints answer { error, code }. The message is already written for
    /// students, so it is shown verbatim and only replaced when the body carries
    /// nothing usable — 401 and 403 come back empty.
    /// </summary>
    private static ApiException ToException(HttpStatusCode status, string body)
    {
        var error = Parse<ApiError>(body);

        var message = error?.Error ?? status switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            // 403 here usually means the university has the Internships module
            // switched off, not that this one listing is private.
            HttpStatusCode.Forbidden => "Internships are not available for your university.",
            HttpStatusCode.NotFound => "That internship is no longer listed.",
            _ => $"The server returned {(int)status}."
        };

        return new ApiException(status, message, error?.Code);
    }
}
