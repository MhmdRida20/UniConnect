using System.Net;

namespace UniConnect.Mobile.Services;

/// <summary>
/// A failed API call, carrying the server's own wording so screens can show it
/// verbatim. The server writes those refusal messages for students already —
/// re-inventing them in the app is how the two clients start telling people
/// different things about the same rule.
/// </summary>
public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Stable machine-readable reason where the server supplies one (for
    /// example CONCURRENCY_RETRY). Lets the app react without matching English.
    /// </summary>
    public string? Code { get; }

    public ApiException(HttpStatusCode statusCode, string message, string? code = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    /// <summary>The token is missing, expired, or rejected — the user must sign in again.</summary>
    public bool IsAuthFailure => StatusCode == HttpStatusCode.Unauthorized;
}
