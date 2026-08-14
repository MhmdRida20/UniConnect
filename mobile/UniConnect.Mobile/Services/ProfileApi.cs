using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>The signed-in user's profile, as the app sees it.</summary>
public class ProfileDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UniversityId { get; set; } = string.Empty;
    public string UniversityCode { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public int MissingFields { get; set; }

    public bool HasPicture => !string.IsNullOrWhiteSpace(ProfilePictureUrl);

    public string Initials => Avatar.Initials(FullName);

    public string PhoneOrPlaceholder =>
        string.IsNullOrWhiteSpace(PhoneNumber) ? "Not set" : PhoneNumber!;
}

/// <summary>
/// Typed client over /api/profile. Same response handling as the other clients:
/// the body is read once to a string and parsed from there.
/// </summary>
public class ProfileApi
{
    private readonly HttpClient _http;

    public ProfileApi(HttpClient http) => _http = http;

    public async Task<ProfileDto> GetAsync(CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.GetAsync("api/profile", ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<ProfileDto>(body) ?? throw Empty("profile");
    }

    public async Task<string> UpdatePhoneAsync(string? phoneNumber, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            () => _http.PutAsJsonAsync("api/profile", new { phoneNumber }, ApiJson.Options, ct), ct);

        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<ActionResponse>(body)?.Message ?? "Profile updated.";
    }

    /// <summary>
    /// Uploads a new picture as multipart, which is what the server's IFormFile
    /// binding expects. Returns the URL of the stored image so the caller can
    /// show it without re-fetching the whole profile.
    /// </summary>
    public async Task<string?> UploadPictureAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);

        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ContentTypeFor(fileName));
        form.Add(file, "file", fileName);

        var (status, body) = await SendAsync(() => _http.PostAsync("api/profile/picture", form, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<PictureResponse>(body)?.ProfilePictureUrl;
    }

    public async Task<string> RemovePictureAsync(CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.DeleteAsync("api/profile/picture", ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);

        return Parse<ActionResponse>(body)?.Message ?? "Profile picture removed.";
    }

    private class PictureResponse : ActionResponse
    {
        public string? ProfilePictureUrl { get; set; }
    }

    /// <summary>
    /// The server only accepts PNG and JPEG, so anything else is labelled by
    /// its extension and refused there rather than guessed at here.
    /// </summary>
    private static string ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg";

    // ---- plumbing ---------------------------------------------------------

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

    private static ApiException ToException(HttpStatusCode status, string body)
    {
        var error = Parse<ApiError>(body);

        var message = error?.Error ?? status switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            HttpStatusCode.RequestEntityTooLarge => "That image is too large — the maximum is 2 MB.",
            _ => $"The server returned {(int)status}."
        };

        return new ApiException(status, message, error?.Code);
    }
}
