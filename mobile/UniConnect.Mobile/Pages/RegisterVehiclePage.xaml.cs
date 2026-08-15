using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class RegisterVehiclePage : ContentPage
{
    private const int MinCapacity = 1;
    private const int MaxCapacity = 8;
    private const int DefaultCapacity = 4;

    private int _capacity = DefaultCapacity;

    private readonly VehiclesApi _api;
    private readonly SessionStore _session;

    public RegisterVehiclePage()
    {
        InitializeComponent();

        _api = ServiceHelper.Get<VehiclesApi>();
        _session = ServiceHelper.Get<SessionStore>();

        UpdateCapacityLabel();
        FormColumn.MaximumWidthRequest = Responsive.FormMaxWidth;
        ActionColumn.MaximumWidthRequest = Responsive.FormMaxWidth;
    }

    private void OnCapacityDown(object? sender, TappedEventArgs e)
    {
        if (_capacity > MinCapacity) _capacity--;
        UpdateCapacityLabel();
    }

    private void OnCapacityUp(object? sender, TappedEventArgs e)
    {
        if (_capacity < MaxCapacity) _capacity++;
        UpdateCapacityLabel();
    }

    private void UpdateCapacityLabel() => CapacityLabel.Text = _capacity.ToString();

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        TypeError.IsVisible = false;
        PlateError.IsVisible = false;
        ColorError.IsVisible = false;

        var type = TypeEntry.Text?.Trim() ?? string.Empty;
        var plate = PlateEntry.Text?.Trim() ?? string.Empty;
        var color = ColorEntry.Text?.Trim() ?? string.Empty;

        var hasError = false;
        if (string.IsNullOrWhiteSpace(type)) { TypeError.Text = "Enter the vehicle type."; TypeError.IsVisible = true; hasError = true; }
        if (string.IsNullOrWhiteSpace(plate)) { PlateError.Text = "Enter the plate number."; PlateError.IsVisible = true; hasError = true; }
        if (string.IsNullOrWhiteSpace(color)) { ColorError.Text = "Enter the vehicle's color."; ColorError.IsVisible = true; hasError = true; }
        if (hasError) return;

        SetBusy(true);
        try
        {
            await _api.CreateAsync(new CreateVehicleRequest
            {
                VehicleType = type,
                PlateNumber = plate,
                Color = color,
                SeatCapacity = _capacity
            });

            await DisplayAlert("Vehicle Registered", "You can now offer rides with this vehicle.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (ApiException ex)
        {
            if (await HandleAuthFailureAsync(ex)) return;
            await DisplayAlert("Couldn't Register Vehicle", ex.Message, "OK");
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
