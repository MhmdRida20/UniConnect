using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Typed client over /api/study-groups — every endpoint the app uses.
///
/// Each response body is read to a string exactly once and then parsed from
/// that string. Reading an HttpContent twice returns an empty stream the second
/// time, which shows up as a mysterious null rather than an error, and the
/// create path genuinely needs two attempts at the same body: once as field
/// errors, once as a plain refusal.
/// </summary>
public class StudyGroupsApi
{
    private readonly HttpClient _http;

    public StudyGroupsApi(HttpClient http) => _http = http;

    // ---- reads ------------------------------------------------------------

    public async Task<List<GroupSummary>> GetGroupsAsync(string? courseCode = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(courseCode)
            ? "api/study-groups"
            : $"api/study-groups?courseCode={Uri.EscapeDataString(courseCode)}";

        return await GetAsync<List<GroupSummary>>(url, ct) ?? new List<GroupSummary>();
    }

    public async Task<GroupDetail> GetGroupAsync(int id, CancellationToken ct = default) =>
        await GetAsync<GroupDetail>($"api/study-groups/{id}", ct)
        ?? throw Empty("group");

    /// <summary>Courses the student is enrolled in — the create form's picker and the list filter.</summary>
    public async Task<List<CourseDto>> GetMyCoursesAsync(CancellationToken ct = default) =>
        await GetAsync<List<CourseDto>>("api/study-groups/my-courses", ct) ?? new List<CourseDto>();

    /// <summary>
    /// Oldest-first, ready to append to the bottom of a chat list.
    /// <paramref name="before"/> pages backwards from a known message id.
    /// </summary>
    public async Task<List<MessageDto>> GetMessagesAsync(
        int groupId, int? before = null, int take = 30, CancellationToken ct = default)
    {
        var url = before is null
            ? $"api/study-groups/{groupId}/messages?take={take}"
            : $"api/study-groups/{groupId}/messages?before={before}&take={take}";

        return await GetAsync<List<MessageDto>>(url, ct) ?? new List<MessageDto>();
    }

    // ---- writes -----------------------------------------------------------

    /// <summary>
    /// Creates a group. Field-level refusals arrive as
    /// <see cref="FieldValidationException"/> so the form can put each message
    /// under the input it belongs to, exactly as the web does.
    /// </summary>
    public async Task<GroupSummary> CreateAsync(CreateGroupRequest request, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            () => _http.PostAsJsonAsync("api/study-groups", request, ApiJson.Options, ct), ct);

        if (IsSuccess(status))
            return Parse<GroupSummary>(body) ?? throw Empty("group");

        if (status == HttpStatusCode.BadRequest)
        {
            var fieldErrors = Parse<FieldErrorResponse>(body);
            if (fieldErrors is { Fields.Count: > 0 })
                throw new FieldValidationException(fieldErrors);
        }

        throw ToException(status, body);
    }

    /// <summary>Returns the server's confirmation wording, which differs for open vs. approval-required groups.</summary>
    public Task<string> JoinAsync(int id, CancellationToken ct = default) =>
        ActionAsync($"api/study-groups/{id}/join", "Request sent.", ct);

    public Task<string> LeaveAsync(int id, CancellationToken ct = default) =>
        ActionAsync($"api/study-groups/{id}/leave", "You have left the group.", ct);

    /// <summary>Creator-only; the server archives the group rather than destroying its history.</summary>
    public async Task<string> DeleteAsync(int id, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.DeleteAsync($"api/study-groups/{id}", ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<ActionResponse>(body)?.Message ?? "Study group deleted.";
    }

    public async Task<MessageDto> PostMessageAsync(int groupId, string content, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            () => _http.PostAsJsonAsync($"api/study-groups/{groupId}/messages", new { content }, ApiJson.Options, ct), ct);

        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<MessageDto>(body) ?? throw Empty("message");
    }

    // ---- member management (creator only; the server enforces that) --------

    public Task<string> ApproveMemberAsync(int memberId, CancellationToken ct = default) =>
        ActionAsync($"api/study-groups/members/{memberId}/approve", "Member approved.", ct);

    public Task<string> RejectMemberAsync(int memberId, CancellationToken ct = default) =>
        ActionAsync($"api/study-groups/members/{memberId}/reject", "Request rejected.", ct);

    public Task<string> RemoveMemberAsync(int memberId, CancellationToken ct = default) =>
        ActionAsync($"api/study-groups/members/{memberId}/remove", "Member removed.", ct);

    public Task<string> TransferLeadershipAsync(int memberId, CancellationToken ct = default) =>
        ActionAsync($"api/study-groups/members/{memberId}/transfer-leadership", "Leadership transferred.", ct);

    // ---- plumbing ---------------------------------------------------------

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var (status, body) = await SendAsync(() => _http.GetAsync(url, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<T>(body);
    }

    private async Task<string> ActionAsync(string url, string fallbackMessage, CancellationToken ct)
    {
        var (status, body) = await SendAsync(() => _http.PostAsync(url, null, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<ActionResponse>(body)?.Message ?? fallbackMessage;
    }

    /// <summary>
    /// Runs the request and drains the body, turning a dead connection into an
    /// ApiException rather than a raw socket error.
    /// </summary>
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
            throw new ApiException(
                HttpStatusCode.RequestTimeout,
                "The server took too long to respond.",
                "TIMEOUT");
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
    /// Study Groups endpoints answer { error: "&lt;sentence&gt;", code: "&lt;code&gt;" }.
    /// The message is written for students already, so it is shown verbatim and
    /// only replaced when the body carries nothing usable — 401 and 403 come
    /// back with empty bodies.
    /// </summary>
    private static ApiException ToException(HttpStatusCode status, string body)
    {
        var error = Parse<ApiError>(body);

        var message = error?.Error ?? status switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            HttpStatusCode.Forbidden => "You do not have access to this study group.",
            HttpStatusCode.NotFound => "That study group no longer exists.",
            _ => $"The server returned {(int)status}."
        };

        return new ApiException(status, message, error?.Code);
    }
}

/// <summary>
/// A 400 carrying per-field messages, so the create form can show each one
/// against its input instead of dumping a single sentence at the top.
/// </summary>
public class FieldValidationException : ApiException
{
    public IReadOnlyList<FieldErrorItem> Fields { get; }

    public FieldValidationException(FieldErrorResponse response)
        : base(HttpStatusCode.BadRequest, response.Error, "VALIDATION")
    {
        Fields = response.Fields;
    }

    /// <summary>Case-insensitive because the server names fields after its own DTO properties.</summary>
    public string? For(string field) =>
        Fields.FirstOrDefault(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase))?.Message;
}
