using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class CreateRidePage : ContentPage
{
    private const int MinSeats = 1;
    private const int MaxSeats = 8;
    private const int DefaultSeats = 3;

    private int _seats = DefaultSeats;
    private List<VehicleDto> _vehicles = new();

    private readonly RidesApi _api;
    private readonly VehiclesApi _vehiclesApi;
    private readonly SessionStore _session;

    public CreateRidePage()
    {
        InitializeComponent();

        _api = ServiceHelper.Get<RidesApi>();
        _vehiclesApi = ServiceHelper.Get<VehiclesApi>();
        _session = ServiceHelper.Get<SessionStore>();

        UpdateSeatsLabel();
        DepartureDatePicker.MinimumDate = DateTime.Today;
        DepartureDatePicker.Date = DateTime.Today;
        DepartureTimePicker.Time = DateTime.Now.AddHours(1).TimeOfDay;

        FormColumn.MaximumWidthRequest = Responsive.FormMaxWidth;
        ActionColumn.MaximumWidthRequest = Responsive.FormMaxWidth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadVehiclesAsync();
    }

    private async Task LoadVehiclesAsync()
    {
        try
        {
            var all = await _vehiclesApi.GetMineAsync();
            _vehicles = all.Where(v => v.Status == "Active").ToList();

            if (_vehicles.Count == 0)
            {
                NoVehicleBox.IsVisible = true;
                FormFields.IsVisible = false;
                return;
            }

            NoVehicleBox.IsVisible = false;
            FormFields.IsVisible = true;

            VehiclePicker.ItemsSource = _vehicles.Select(v => v.DisplayLabel).ToList();
            VehiclePicker.SelectedIndex = 0;
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            await DisplayAlert("Couldn't Load Vehicles", ex.Message, "OK");
        }
    }

    private void OnVehicleChanged(object? sender, EventArgs e)
    {
        if (VehiclePicker.SelectedIndex < 0 || VehiclePicker.SelectedIndex >= _vehicles.Count) return;

        // Seats can never exceed the selected vehicle's own capacity —
        // matches RideService.CreateRideAsync's server-side check, clamped
        // here too so the request never gets sent doomed to fail.
        var capacity = _vehicles[VehiclePicker.SelectedIndex].SeatCapacity;
        if (_seats > capacity) _seats = capacity;
        UpdateSeatsLabel();
    }

    private void OnSeatsDown(object? sender, TappedEventArgs e)
    {
        if (_seats > MinSeats) _seats--;
        UpdateSeatsLabel();
    }

    private void OnSeatsUp(object? sender, TappedEventArgs e)
    {
        var cap = VehiclePicker.SelectedIndex >= 0 && VehiclePicker.SelectedIndex < _vehicles.Count
            ? _vehicles[VehiclePicker.SelectedIndex].SeatCapacity
            : MaxSeats;
        if (_seats < Math.Min(MaxSeats, cap)) _seats++;
        UpdateSeatsLabel();
    }

    private void UpdateSeatsLabel() => SeatsLabel.Text = _seats.ToString();

    private async void OnRegisterVehicleClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(RegisterVehiclePage));

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        ClearErrors();

        var departure = DepartureEntry.Text?.Trim() ?? string.Empty;
        var destination = DestinationEntry.Text?.Trim() ?? string.Empty;
        var departureDateTime = DepartureDatePicker.Date.Date + DepartureTimePicker.Time;

        var hasError = false;

        if (string.IsNullOrWhiteSpace(departure))
        {
            DepartureError.Text = "Enter where you're leaving from.";
            DepartureError.IsVisible = true;
            hasError = true;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            DestinationError.Text = "Enter your destination.";
            DestinationError.IsVisible = true;
            hasError = true;
        }
        else if (string.Equals(departure, destination, StringComparison.OrdinalIgnoreCase))
        {
            DestinationError.Text = "Destination must be different from the departure location.";
            DestinationError.IsVisible = true;
            hasError = true;
        }

        if (departureDateTime <= DateTime.Now)
        {
            DepartureTimeError.Text = "Departure time must be in the future.";
            DepartureTimeError.IsVisible = true;
            hasError = true;
        }

        if (VehiclePicker.SelectedIndex < 0)
        {
            VehicleError.Text = "Select a vehicle.";
            VehicleError.IsVisible = true;
            hasError = true;
        }

        if (hasError) return;

        var vehicle = _vehicles[VehiclePicker.SelectedIndex];

        SetBusy(true);
        try
        {
            var rideId = await _api.CreateAsync(new CreateRideRequest
            {
                DepartureLocation = departure,
                Destination = destination,
                DepartureTime = departureDateTime,
                VehicleId = vehicle.Id,
                TotalSeats = _seats,
                Notes = NotesEditor.Text
            });

            await DisplayAlert("Ride Posted", "Your ride is now visible to other students.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            await DisplayAlert("Couldn't Post Ride", ex.Message, "OK");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearErrors()
    {
        DepartureError.IsVisible = false;
        DestinationError.IsVisible = false;
        DepartureTimeError.IsVisible = false;
        VehicleError.IsVisible = false;
        SeatsError.IsVisible = false;
    }

    private void SetBusy(bool busy)
    {
        Busy.IsRunning = busy;
        Busy.IsVisible = busy;
        SubmitButton.IsEnabled = !busy;
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
