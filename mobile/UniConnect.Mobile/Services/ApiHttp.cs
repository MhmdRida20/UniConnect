using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Builds the <see cref="HttpClient"/> the app uses to reach the API, including
/// the one concession local development needs: trusting ASP.NET's development
/// certificate. The SignalR connection reuses the same rule, so there is only
/// one place where certificate validation is relaxed.
/// </summary>
public static class ApiHttp
{
    public static HttpClient CreateClient(SessionStore session)
    {
        // Bearer token goes on here, at the outermost layer, so every call made
        // through this client is authenticated without the callers thinking
        // about it.
        var authenticated = new AuthHeaderHandler(session) { InnerHandler = CreateHandler() };

        return new HttpClient(authenticated)
        {
            BaseAddress = ApiConfig.BaseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>
    /// The certificate rule for development hosts, or null in a release build
    /// and whenever the app is pointed at a real server. Null means "validate
    /// normally" — callers must not substitute an always-true check.
    ///
    /// "dotnet dev-certs https --trust" only installs the certificate into the
    /// development PC's trust store, which is why the Windows build and a
    /// desktop browser are happy with it. An Android device has its own store
    /// and has never heard of it, and the certificate is issued for "localhost"
    /// anyway — so it would be rejected on both counts when reached at
    /// 10.0.2.2 or over the LAN.
    /// </summary>
    public static Func<HttpRequestMessage?, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? DevCertificateCheck
    {
        get
        {
#if DEBUG
            if (!ApiConfig.IsLocalDevelopmentHost) return null;

            return (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None
                || certificate?.Issuer.Contains("localhost", StringComparison.OrdinalIgnoreCase) == true;
#else
            // No way to switch this on in a shipped build.
            return null;
#endif
        }
    }

    /// <summary>
    /// Applies <see cref="DevCertificateCheck"/> to a handler SignalR built for
    /// itself, rather than replacing that handler wholesale — negotiate relies
    /// on how SignalR configured it.
    /// </summary>
    public static HttpMessageHandler ApplyDevCertificate(HttpMessageHandler handler)
    {
        var check = DevCertificateCheck;
        if (check is null) return handler;

#if ANDROID
        if (handler is Xamarin.Android.Net.AndroidMessageHandler android)
            android.ServerCertificateCustomValidationCallback = new(check);
#else
        if (handler is HttpClientHandler client)
            client.ServerCertificateCustomValidationCallback = new(check);
#endif

        return handler;
    }

    private static HttpMessageHandler CreateHandler()
    {
#if ANDROID
        // The default handler on Android is already AndroidMessageHandler, but
        // naming it explicitly is what makes the certificate callback below
        // reachable from shared code.
        HttpMessageHandler handler = new Xamarin.Android.Net.AndroidMessageHandler();
#else
        HttpMessageHandler handler = new HttpClientHandler();
#endif

        return ApplyDevCertificate(handler);
    }
}
