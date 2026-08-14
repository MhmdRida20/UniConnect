namespace UniConnect.Mobile.Services;

/// <summary>
/// Holds the signed-in user's profile for the whole app.
///
/// Every screen's app bar draws the same avatar, so without this each one would
/// fetch the profile on every appearance — and, worse, a picture changed on the
/// profile screen would not show on the others until they happened to reload.
/// One cached copy plus a change event keeps all of them in step.
/// </summary>
public class ProfileStore
{
    private readonly ProfileApi _api;

    public ProfileStore(ProfileApi api) => _api = api;

    /// <summary>The last known profile, or null before the first successful load.</summary>
    public ProfileDto? Current { get; private set; }

    /// <summary>Raised whenever Current changes, so open screens can redraw.</summary>
    public event Action? Changed;

    /// <summary>
    /// Returns the cached profile, fetching it once if this is the first ask.
    /// <paramref name="refresh"/> forces a re-read after an edit.
    /// </summary>
    public async Task<ProfileDto?> GetAsync(bool refresh = false)
    {
        if (Current is not null && !refresh) return Current;

        try
        {
            Set(await _api.GetAsync());
        }
        catch (ApiException)
        {
            // The avatar is decoration on most screens; a failure to load it
            // must not take the screen down with it. Whoever actually needs the
            // profile — the profile screen — calls the API directly and shows
            // the error itself.
        }

        return Current;
    }

    public void Set(ProfileDto profile)
    {
        Current = profile;
        Changed?.Invoke();
    }

    /// <summary>Called on sign-out so the next user does not inherit this avatar.</summary>
    public void Clear()
    {
        Current = null;
        Changed?.Invoke();
    }
}
