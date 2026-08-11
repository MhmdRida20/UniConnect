using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Holds the signed-in student's token between app launches so they are not
/// asked to log in every time the app starts.
/// </summary>
public class SessionStore
{
    private const string StorageKey = "uniconnect.session";

    /// <summary>What we keep about the signed-in student. Nothing sensitive beyond the token itself.</summary>
    public record Session(
        string Token,
        DateTime ExpiresAtUtc,
        string UserId,
        string FullName,
        string Email,
        string UniversityCode);

    // Read on every outgoing request via AuthHeaderHandler, so the decrypt is
    // worth doing once rather than per call.
    private Session? _cached;

    public async Task<Session?> GetAsync()
    {
        if (_cached is not null) return _cached;

        var raw = await ReadAsync(StorageKey);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            _cached = JsonSerializer.Deserialize<Session>(raw);
        }
        catch (JsonException)
        {
            // Stored by an older build with a different shape. Nothing to
            // salvage; drop it and let the user sign in again.
            await ClearAsync();
            return null;
        }

        return _cached;
    }

    /// <summary>
    /// The bearer token, or null when there isn't a usable one. Expiry is
    /// checked here only to skip a round trip that would certainly 401 — the
    /// server's validation remains the thing that actually decides.
    /// </summary>
    public async Task<string?> GetTokenAsync()
    {
        var session = await GetAsync();
        if (session is null) return null;

        // Small margin so a token doesn't expire mid-flight.
        return session.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(30) ? session.Token : null;
    }

    public async Task<bool> HasValidTokenAsync() => await GetTokenAsync() is not null;

    public async Task SaveAsync(AuthResponse response)
    {
        _cached = new Session(
            response.Token,
            response.ExpiresAtUtc,
            response.UserId,
            response.FullName,
            response.Email,
            response.UniversityCode);

        await WriteAsync(StorageKey, JsonSerializer.Serialize(_cached));
    }

    public Task ClearAsync()
    {
        _cached = null;
        try
        {
            SecureStorage.Default.Remove(StorageKey);
        }
        catch (Exception)
        {
            // Ignored for the same reason the writes below fall back.
        }
        Preferences.Default.Remove(StorageKey);
        return Task.CompletedTask;
    }

    // ---- storage, with a development-only fallback ------------------------
    //
    // SecureStorage is the right home for a bearer token: Android puts it in
    // the KeyStore and iOS in the Keychain. Windows is the awkward one — its
    // implementation reaches for packaged-app storage, and this project builds
    // unpackaged (WindowsPackageType=None) so it can run without Developer
    // Mode. Rather than let that throw on the desktop development target, fall
    // back to Preferences.
    //
    // Preferences is NOT encrypted, so this is a real downgrade — but it only
    // ever happens on the Windows development build. Android, the platform
    // that actually ships, always takes the SecureStorage path.

    private static async Task<string?> ReadAsync(string key)
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(key);
            if (!string.IsNullOrEmpty(value)) return value;
        }
        catch (Exception)
        {
            // Fall through to the plain-text fallback below.
        }

        var fallback = Preferences.Default.Get(key, string.Empty);
        return string.IsNullOrEmpty(fallback) ? null : fallback;
    }

    private static async Task WriteAsync(string key, string value)
    {
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
            return;
        }
        catch (Exception)
        {
            // Fall through.
        }

        Preferences.Default.Set(key, value);
    }
}
