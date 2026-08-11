using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>Wraps POST /api/auth/login and stores the resulting session.</summary>
public class AuthApi
{
    private readonly HttpClient _http;
    private readonly SessionStore _session;

    public AuthApi(HttpClient http, SessionStore session)
    {
        _http = http;
        _session = session;
    }

    /// <summary>
    /// Signs in and persists the token. Throws <see cref="ApiException"/> with
    /// the server's own message on any refusal.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("api/auth/login", new { email, password }, ct);
        }
        catch (HttpRequestException ex)
        {
            // Nothing answered at all — wrong address, server not started, or
            // the phone is not on the same network. Worth saying so plainly
            // rather than reporting it as a sign-in failure.
            throw new ApiException(
                HttpStatusCode.ServiceUnavailable,
                $"Could not reach the server at {ApiConfig.BaseAddress}. {ex.GetBaseException().Message}",
                "NO_CONNECTION");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options, ct);
                if (auth is null || string.IsNullOrEmpty(auth.Token))
                    throw new ApiException(response.StatusCode, "The server did not return a sign-in token.");

                await _session.SaveAsync(auth);
                return auth;
            }

            throw await ToExceptionAsync(response, ct);
        }
    }

    public Task SignOutAsync() => _session.ClearAsync();

    /// <summary>
    /// Auth endpoints answer { error: "&lt;code&gt;", message: "&lt;sentence&gt;" },
    /// which is the opposite way round from the Study Groups endpoints. Reading
    /// the wrong field here is how "invalid_credentials" ends up on screen
    /// instead of "Incorrect email or password."
    /// </summary>
    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? message = null;
        string? code = null;

        try
        {
            var error = await response.Content.ReadFromJsonAsync<AuthError>(ApiJson.Options, ct);
            message = error?.Message;
            code = error?.Error;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            // ValidationProblem returns an RFC 7807 body instead; fall back to
            // the status-based wording below.
        }

        message ??= response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Incorrect email or password.",
            HttpStatusCode.Forbidden => "This account cannot use the mobile app.",
            (HttpStatusCode)423 => "Too many failed attempts. Please try again later.",
            HttpStatusCode.BadRequest => "Please check the email and password you entered.",
            _ => $"Sign-in failed ({(int)response.StatusCode})."
        };

        return new ApiException(response.StatusCode, message, code);
    }
}
