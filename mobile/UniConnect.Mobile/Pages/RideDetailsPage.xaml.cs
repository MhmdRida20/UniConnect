using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

[QueryProperty(nameof(RideIdParameter), "id")]
public partial class RideDetailsPage : ContentPage
{
    private readonly RidesApi _api;
    private readonly SessionStore _session;

    private int _rideId;
    private RideDetailsDto? _ride;

    public string RideIdParameter
    {
        set
        {
            if (int.TryParse(value, out var id)) _rideId = id;
        }
    }

    public RideDetailsPage()
    {
        InitializeComponent();

        _api = ServiceHelper.Get<RidesApi>();
        _session = ServiceHelper.Get<SessionStore>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync();
        Refresher.IsRefreshing = false;
    }

    private async Task LoadAsync()
    {
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        ErrorLabel.IsVisible = false;
        ContentStack.IsVisible = false;

        try
        {
            _ride = await _api.GetDetailsAsync(_rideId);
            Render(_ride);
            ContentStack.IsVisible = true;
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            ErrorLabel.Text = ex.Message;
            ErrorLabel.IsVisible = true;
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private void Render(RideDetailsDto ride)
    {
        RouteLabel.Text = $"{ride.DepartureLocation}  \u2192  {ride.Destination}";
        WhenLabel.Text = ride.DepartureLabel;
        DriverLabel.Text = $"Driver: {ride.Driver.FullName}" + (ride.Vehicle is null ? "" : $"  \u00b7  {ride.Vehicle.VehicleType} ({ride.Vehicle.Color}, plate {ride.Vehicle.PlateNumber})");
        SeatsLabel.Text = $"{ride.AvailableSeats} of {ride.TotalSeats} seats available  \u00b7  Status: {ride.Status}";

        if (!string.IsNullOrWhiteSpace(ride.Notes))
        {
            NotesLabel.Text = $"Notes: {ride.Notes}";
            NotesLabel.IsVisible = true;
        }

        if (ride.IsDriver)
        {
            PassengerSection.IsVisible = false;
            MyRequestBox.IsVisible = false;
            DriverSection.IsVisible = true;
            BuildRequestsList(ride);
        }
        else if (ride.IAlreadyRequested)
        {
            PassengerSection.IsVisible = false;
            DriverSection.IsVisible = false;
            MyRequestBox.IsVisible = true;
            MyRequestStatusLabel.Text = ride.MyRequestStatus == "Accepted"
                ? "Your request was accepted \u2014 you have a seat on this ride."
                : "Your request is waiting on the driver.";
        }
        else
        {
            DriverSection.IsVisible = false;
            MyRequestBox.IsVisible = false;
            PassengerSection.IsVisible = ride.Status == "Active" && ride.AvailableSeats > 0;
            if (!PassengerSection.IsVisible)
            {
                ErrorLabel.Text = "This ride is no longer accepting requests.";
                ErrorLabel.IsVisible = true;
            }
        }
    }

    private void BuildRequestsList(RideDetailsDto ride)
    {
        RequestsStack.Children.Clear();
        var pending = ride.Requests.Where(r => r.Status is "Pending" or "Accepted").ToList();
        NoRequestsLabel.IsVisible = pending.Count == 0;

        foreach (var req in pending)
            RequestsStack.Children.Add(BuildRequestCard(req));
    }

    private Border BuildRequestCard(RideRequestDto req)
    {
        var nameLabel = new Label { Text = req.PassengerName, Style = (Style)Application.Current!.Resources["UcH2"] };
        var pickupLabel = new Label { Text = $"Pickup: {req.PickupLocation}", Style = (Style)Application.Current!.Resources["UcMutedText"] };

        Layout actions;
        if (req.Status == "Pending")
        {
            var acceptBtn = new Button { Text = "Accept", Style = (Style)Application.Current!.Resources["UcBtnPrimaryLarge"], HeightRequest = 40 };
            var rejectBtn = new Button { Text = "Reject", Style = (Style)Application.Current!.Resources["UcBtnOutline"], HeightRequest = 40 };
            acceptBtn.Clicked += async (_, _) => await HandleRequestActionAsync(() => _api.AcceptRequestAsync(req.Id));
            rejectBtn.Clicked += async (_, _) => await HandleRequestActionAsync(() => _api.RejectRequestAsync(req.Id));
            actions = new HorizontalStackLayout { Spacing = 10, Children = { acceptBtn, rejectBtn } };
        }
        else
        {
            actions = new HorizontalStackLayout
            {
                Children = { new Label { Text = "Accepted", TextColor = Color.FromArgb("#15803d"), FontAttributes = FontAttributes.Bold } }
            };
        }

        var stack = new VerticalStackLayout { Spacing = 6, Children = { nameLabel, pickupLabel, actions } };
        return new Border { Style = (Style)Application.Current!.Resources["UcCardSoft"], Content = stack };
    }

    private async Task HandleRequestActionAsync(Func<Task> action)
    {
        try
        {
            await action();
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            await DisplayAlert("Couldn't Complete That", ex.Message, "OK");
        }
    }

    private async void OnRequestClicked(object? sender, EventArgs e)
    {
        var pickup = PickupEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pickup))
        {
            await DisplayAlert("Pickup Location Needed", "Please enter where the driver should pick you up.", "OK");
            return;
        }

        try
        {
            await _api.RequestRideAsync(_rideId, pickup);
            await DisplayAlert("Request Sent", "Your request was sent to the driver.", "OK");
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            await DisplayAlert("Couldn't Send Request", ex.Message, "OK");
        }
    }

    private async void OnCancelRequestClicked(object? sender, EventArgs e)
    {
        try
        {
            var confirm = await DisplayAlert("Cancel Request", "Cancel your request for this ride?", "Yes, Cancel", "No");
            if (!confirm) return;

            // The passenger doesn't know their own RideRequest id from this
            // screen — request it fresh via mine() rather than guessing.
            var mine = await _api.GetMineAsync();
            var found = mine.Requested.FirstOrDefault(r => r.RideId == _rideId);
            if (found is null)
            {
                await DisplayAlert("Not Found", "Couldn't find your request for this ride.", "OK");
                return;
            }

            await _api.CancelRequestAsync(found.RequestId);
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            await DisplayAlert("Couldn't Cancel", ex.Message, "OK");
        }
    }

    private async void OnCancelRideClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlert("Cancel This Ride", "This cancels the ride for every passenger. Continue?", "Yes, Cancel", "No");
        if (!confirm) return;

        try
        {
            await _api.CancelRideAsync(_rideId);
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            await DisplayAlert("Couldn't Cancel", ex.Message, "OK");
        }
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
