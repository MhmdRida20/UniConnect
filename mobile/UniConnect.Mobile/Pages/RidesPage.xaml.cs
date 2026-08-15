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
        var routeLabel = new Label
        {
            Text = $"{ride.DepartureLocation}  \u2192  {ride.Destination}",
            Style = (Style)Application.Current!.Resources["UcH2"]
        };
        var whenLabel = new Label { Text = ride.DepartureLabel, Style = (Style)Application.Current!.Resources["UcMutedText"] };
        var driverLabel = new Label
        {
            Text = $"Driver: {ride.Driver.FullName}" + (ride.Vehicle is null ? "" : $"  \u00b7  {ride.Vehicle.VehicleType} ({ride.Vehicle.Color})"),
            Style = (Style)Application.Current!.Resources["UcTiny"]
        };
        var seatsLabel = new Label
        {
            Text = $"{ride.AvailableSeats} of {ride.TotalSeats} seats free",
            Style = (Style)Application.Current!.Resources["UcTiny"]
        };

        var textStack = new VerticalStackLayout { Spacing = 3, Children = { routeLabel, whenLabel, driverLabel, seatsLabel } };

        Frame? statusPill = null;
        if (ride.IAlreadyRequested)
        {
            var (bg, fg, label) = ride.MyRequestStatus == "Accepted"
                ? ("#dcfce7", "#15803d", "Accepted")
                : ("#fef3c7", "#92400e", "Requested");
            statusPill = new Frame
            {
                Padding = new Thickness(10, 4),
                CornerRadius = 999,
                HasShadow = false,
                BackgroundColor = Color.FromArgb(bg),
                Content = new Label { Text = label, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(fg) }
            };
        }

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(textStack, 0);
        if (statusPill != null) grid.Add(statusPill, 1);

        var card = new Border { Style = (Style)Application.Current!.Resources["UcCardSoft"], Content = grid };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Shell.Current.GoToAsync($"{nameof(RideDetailsPage)}?id={ride.Id}");
        card.GestureRecognizers.Add(tap);

        return card;
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

    private async void OnProfileTabTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync(nameof(ProfilePage));

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
