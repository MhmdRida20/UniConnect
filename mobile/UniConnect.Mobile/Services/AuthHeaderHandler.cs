using System.Net.Http.Headers;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Attaches the stored bearer token to every outgoing request, so no screen or
/// API client has to remember to. A request that goes out without one simply
/// comes back 401 and the app signs the user out.
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly SessionStore _session;

    public AuthHeaderHandler(SessionStore session) => _session = session;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Login itself must not carry a stale token, and an explicit header
        // set by a caller wins.
        if (request.Headers.Authorization is null)
        {
            var token = await _session.GetTokenAsync();
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
