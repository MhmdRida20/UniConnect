using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

[QueryProperty(nameof(ScannedToken), "scanned")]
public partial class AttendanceCheckInPage : ContentPage
{
    private readonly AttendanceApi _api;
    private readonly SessionStore _session;

    private string? _confirmedToken;

    /// <summary>
    /// Set by Shell when ScanPage navigates back here with "?scanned=...".
    /// A property rather than a constructor parameter because Shell
    /// constructs this page itself (see AppShell's RegisterRoute) — it can
    /// set a property on the result, but it cannot pass constructor arguments.
    /// </summary>
    public string ScannedToken
    {
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            TokenEntry.Text = Uri.UnescapeDataString(value);
            _ = CheckCodeAsync(TokenEntry.Text);
        }
    }

    public AttendanceCheckInPage()
    {
        InitializeComponent();

        _api = ServiceHelper.Get<AttendanceApi>();
        _session = ServiceHelper.Get<SessionStore>();
    }

    private void OnTokenTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Typing again after a confirmed session invalidates it — otherwise
        // "Confirm Attendance" could submit a session that no longer matches
        // what is in the box.
        if (SessionCard.IsVisible)
        {
            SessionCard.IsVisible = false;
            ConfirmBtn.IsVisible = false;
            _confirmedToken = null;
        }
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Camera Needed", "Camera permission is required to scan a QR code.", "OK");
            return;
        }

        await Shell.Current.GoToAsync(nameof(ScanPage));
    }

    private async void OnCheckCodeClicked(object? sender, EventArgs e) =>
        await CheckCodeAsync(TokenEntry.Text);

    private async Task CheckCodeAsync(string? rawToken)
    {
        var token = (rawToken ?? string.Empty).Trim();
        TokenError.IsVisible = false;
        SessionCard.IsVisible = false;
        ConfirmBtn.IsVisible = false;

        if (string.IsNullOrWhiteSpace(token))
        {
            TokenError.Text = "Enter or scan a code first.";
            TokenError.IsVisible = true;
            return;
        }

        SetBusy(true);
        try
        {
            var info = await _api.GetSessionInfoAsync(token);

            if (!info.Found)
            {
                TokenError.Text = info.Error ?? "That code isn't valid.";
                TokenError.IsVisible = true;
                return;
            }

            if (!string.IsNullOrEmpty(info.Error))
            {
                TokenError.Text = info.Error;
                TokenError.IsVisible = true;
                return;
            }

            _confirmedToken = token;
            SessionCourseLabel.Text = $"{info.CourseCode} — {info.CourseName}";
            SessionTimeLabel.Text = info.TimeRangeLabel;
            SessionCard.IsVisible = true;
            ConfirmBtn.IsVisible = true;
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            TokenError.Text = ex.Message;
            TokenError.IsVisible = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_confirmedToken)) return;

        var locationStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (locationStatus != PermissionStatus.Granted)
        {
            await DisplayAlert("Location Needed", "Location access is required to confirm attendance.", "OK");
            return;
        }

        SetBusy(true);

        Location? location;
        try
        {
            location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(15)));
        }
        catch (Exception ex)
        {
            SetBusy(false);
            await DisplayAlert("Location Error", $"Couldn't get your location: {ex.Message}", "OK");
            return;
        }

        if (location is null)
        {
            SetBusy(false);
            await DisplayAlert("Location Unavailable", "Couldn't determine your location. Make sure location services are on.", "OK");
            return;
        }

        try
        {
            var result = await _api.SubmitAsync(_confirmedToken, location.Latitude, location.Longitude);

            if (result.Success)
            {
                await DisplayAlert("Checked In", result.Message, "OK");
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                await DisplayAlert("Couldn't Check In", result.Message, "OK");
            }
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            await DisplayAlert("Couldn't Check In", ex.Message, "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
        CheckCodeBtn.IsEnabled = !busy;
        ConfirmBtn.IsEnabled = !busy;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("..");

    private async Task<bool> HandleAuthFailureAsync(ApiException ex)
    {
        if (!ex.IsAuthFailure) return false;

        await _session.ClearAsync();
        await Shell.Current.GoToAsync("//login");
        return true;
    }
}
