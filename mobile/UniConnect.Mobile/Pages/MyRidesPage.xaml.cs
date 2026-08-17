using UniConnect.Mobile.Controls;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class MyRidesPage : ContentPage
{
    private readonly RidesApi _api;
    private readonly SessionStore _session;

    public MyRidesPage()
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
        DrivingStack.Children.Clear();
        RequestedStack.Children.Clear();

        try
        {
            var mine = await _api.GetMineAsync();

            // the count sits in the heading so a long list still tells you how
            // long it is without scrolling to the bottom of it
            DrivingHeader.Text = mine.Driving.Count > 0
                ? $"Rides I'm Driving  ({mine.Driving.Count})"
                : "Rides I'm Driving";
            NoDrivingLabel.IsVisible = mine.Driving.Count == 0;
            foreach (var ride in mine.Driving)
                DrivingStack.Children.Add(BuildDrivingCard(ride));

            RequestedHeader.Text = mine.Requested.Count > 0
                ? $"Rides I've Requested  ({mine.Requested.Count})"
                : "Rides I've Requested";
            NoRequestedLabel.IsVisible = mine.Requested.Count == 0;
            foreach (var req in mine.Requested)
                RequestedStack.Children.Add(BuildRequestedCard(req));

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

    private Border BuildDrivingCard(RideListItemDto ride)
    {
        var (bg, fg) = ride.Status switch
        {
            "Active" => ("#dcfce7", "#15803d"),
            "Full" => ("#dbeafe", "#1e40af"),
            "Completed" => ("#f1f5f9", "#334155"),
            _ => ("#fee2e2", "#991b1b")
        };

        return RideCardParts.Card(
            ride.DepartureLocation, ride.Destination,
            (ride.Status, bg, fg),
            ride.DepartureLabel,
            $"{ride.AvailableSeats} of {ride.TotalSeats} seats free",
            () => Shell.Current.GoToAsync($"{nameof(RideDetailsPage)}?id={ride.Id}"));
    }

    private Border BuildRequestedCard(RideRequestSummaryDto req)
    {
        var (bg, fg) = req.Status switch
        {
            "Accepted" => ("#dcfce7", "#15803d"),
            "Rejected" => ("#fee2e2", "#991b1b"),
            _ => ("#fef3c7", "#92400e")
        };

        return RideCardParts.Card(
            req.DepartureLocation, req.Destination,
            (req.Status, bg, fg),
            req.DepartureLabel,
            $"Driver: {req.DriverName}",
            () => Shell.Current.GoToAsync($"{nameof(RideDetailsPage)}?id={req.RideId}"));
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
