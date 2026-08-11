namespace UniConnect.Mobile.Services;

/// <summary>
/// Works out where the UniConnect web API lives for whatever the app happens to
/// be running on. "localhost" means something different on every target — on an
/// Android emulator it is the emulator itself, not the PC hosting it — so the
/// address has to be resolved at runtime rather than hard-coded.
/// </summary>
public static class ApiConfig
{
    /// <summary>
    /// Your development PC's address on the local network. Only used when the
    /// app runs on a real phone, which has no route back to the PC otherwise.
    /// Find it with <c>ipconfig</c> (the IPv4 Address of your Wi-Fi adapter);
    /// the phone and the PC must be on the same network. It changes whenever
    /// the router hands out a new lease, so expect to update it occasionally.
    /// </summary>
    public const string LanHost = "172.16.124.104";

    /// <summary>
    /// The alias the Android emulator maps to its host machine's loopback.
    /// 127.0.0.1 inside the emulator is the emulated device itself.
    /// </summary>
    private const string EmulatorHost = "10.0.2.2";

    /// <summary>Ports from the web project's "https" launch profile.</summary>
    public const int HttpsPort = 7253;
    public const int HttpPort = 5007;

    /// <summary>
    /// Whether to talk HTTPS. Leave this on for the "https" launch profile.
    ///
    /// The web app calls <c>UseHttpsRedirection()</c> unconditionally, so a
    /// plain-HTTP request to /api answers 307 pointing at the HTTPS port. That
    /// looks survivable — HttpClient follows redirects — but the port change
    /// makes it a different origin, and HttpClient strips the Authorization
    /// header when it crosses one. Every signed-in call would come back 401
    /// looking exactly like an expired token. So: HTTPS, not cleartext.
    ///
    /// The one case for turning this off is the "http" launch profile, where no
    /// HTTPS port exists, the redirect middleware gives up, and plain HTTP is
    /// served as-is. Android permits that only for the hosts listed in
    /// Platforms/Android/Resources/xml/network_security_config.xml.
    /// </summary>
    public static bool UseHttps { get; set; } = true;

    /// <summary>The host portion of the API address for this device.</summary>
    public static string Host
    {
        get
        {
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
                return "localhost";

            // A physical phone has to come in over the LAN; only the emulator
            // gets the 10.0.2.2 shortcut to the host's loopback.
            if (DeviceInfo.Current.Platform == DevicePlatform.Android)
                return DeviceInfo.Current.DeviceType == DeviceType.Virtual ? EmulatorHost : LanHost;

            return LanHost;
        }
    }

    /// <summary>Base address for the API, with the trailing slash HttpClient wants.</summary>
    public static Uri BaseAddress =>
        new($"{(UseHttps ? "https" : "http")}://{Host}:{(UseHttps ? HttpsPort : HttpPort)}/");

    /// <summary>
    /// True when the app is pointed at a development machine rather than a
    /// deployed server. Guards the certificate exemption in <see cref="ApiHttp"/>.
    /// </summary>
    public static bool IsLocalDevelopmentHost =>
        Host is "localhost" or EmulatorHost || Host == LanHost;
}
