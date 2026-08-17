using UniConnect.Mobile.Controls;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class RidesPage : ContentPage
{
    private readonly RidesApi _api;
    private readonly SessionStore _session;
    private readonly NotificationsApi _notifications;
    private readonly ProfileStore _profiles;

    public RidesPage()
    {
        InitializeComponent();

        _api = ServiceHelper.Get<RidesApi>();
        _session = ServiceHelper.Get<SessionStore>();
        _notifications = ServiceHelper.Get<NotificationsApi>();
        _profiles = ServiceHelper.Get<ProfileStore>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyProfile(await _profiles.GetAsync());
        await LoadUnreadCountAsync();
        await LoadRidesAsync();
    }

    private void ApplyProfile(ProfileDto? profile)
    {
        ProfileInitials.Text = profile?.Initials ?? "?";
        ProfilePhoto.Source = profile?.HasPicture == true
            ? ImageSource.FromUri(new Uri(profile.ProfilePictureUrl!))
            : null;
        ProfilePhoto.IsVisible = profile?.HasPicture == true;
    }

    private async Task LoadUnreadCountAsync()
    {
        var count = await _notifications.GetUnreadCountAsync();
        UnreadBadge.IsVisible = count > 0;
        UnreadBadgeLabel.Text = count > 99 ? "99+" : count.ToString();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadRidesAsync();
        Refresher.IsRefreshing = false;
    }

    private async Task LoadRidesAsync()
    {
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        ErrorBox.IsVisible = false;
        EmptyState.IsVisible = false;
        RidesStack.Children.Clear();

        try
        {
            var rides = await _api.GetAvailableAsync();

            if (rides.Count == 0)
            {
                EmptyState.IsVisible = true;
            }
            else
            {
                foreach (var ride in rides)
                    RidesStack.Children.Add(BuildRideCard(ride));
            }
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            ErrorLabel.Text = ex.Message;
            ErrorBox.IsVisible = true;
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private Border BuildRideCard(RideListItemDto ride)
    {
        // Same card as My Rides, from the same helper \u2014 see RideCardParts for
        // why the chip is a Border and why the route is two truncated lines.
        (string, string, string)? status = ride.IAlreadyRequested
            ? (ride.MyRequestStatus == "Accepted"
                ? ("Accepted", "#dcfce7", "#15803d")
                : ("Requested", "#fef3c7", "#92400e"))
            : null;

        var driver = $"{ride.Driver.FullName}"
            + (ride.Vehicle is null ? "" : $"  \u00b7  {ride.Vehicle.VehicleType}");

        return RideCardParts.Card(
            ride.DepartureLocation, ride.Destination,
            status,
            $"{ride.DepartureLabel}  \u00b7  {driver}",
            $"{ride.AvailableSeats} of {ride.TotalSeats} seats free",
            () => Shell.Current.GoToAsync($"{nameof(RideDetailsPage)}?id={ride.Id}"));
    }

    private async void OnOfferRideClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(CreateRidePage));

    private async void OnMyRidesClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(MyRidesPage));

    private async void OnNotificationsClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(NotificationsPage));

    private async void OnHomeTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("//home");
    private async void OnGroupsTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("//groups");
    private async void OnInternshipsTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("//internships");
    private async void OnAttendanceTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("//attendance");

    private async void OnProfileTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync(nameof(ProfilePage));

    private async Task<bool> HandleAuthFailureAsync(ApiException ex)
    {
        if (!ex.IsAuthFailure) return false;
        await _session.ClearAsync();
        await Shell.Current.GoToAsync("//login");
        return true;
    }
}

