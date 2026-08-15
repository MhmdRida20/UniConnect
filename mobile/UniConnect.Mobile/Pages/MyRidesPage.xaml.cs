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

            NoDrivingLabel.IsVisible = mine.Driving.Count == 0;
            foreach (var ride in mine.Driving)
                DrivingStack.Children.Add(BuildDrivingCard(ride));

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

        var textStack = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                new Label { Text = $"{ride.DepartureLocation}  \u2192  {ride.Destination}", Style = (Style)Application.Current!.Resources["UcH2"] },
                new Label { Text = ride.DepartureLabel, Style = (Style)Application.Current!.Resources["UcMutedText"] },
                new Label { Text = $"{ride.AvailableSeats} of {ride.TotalSeats} seats free", Style = (Style)Application.Current!.Resources["UcTiny"] }
            }
        };

        var statusPill = new Frame
        {
            Padding = new Thickness(10, 4),
            CornerRadius = 999,
            HasShadow = false,
            BackgroundColor = Color.FromArgb(bg),
            Content = new Label { Text = ride.Status, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(fg) }
        };

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(textStack, 0);
        grid.Add(statusPill, 1);

        var card = new Border { Style = (Style)Application.Current!.Resources["UcCardSoft"], Content = grid };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Shell.Current.GoToAsync($"{nameof(RideDetailsPage)}?id={ride.Id}");
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private Border BuildRequestedCard(RideRequestSummaryDto req)
    {
        var (bg, fg) = req.Status switch
        {
            "Accepted" => ("#dcfce7", "#15803d"),
            "Rejected" => ("#fee2e2", "#991b1b"),
            _ => ("#fef3c7", "#92400e")
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                new Label { Text = $"{req.DepartureLocation}  \u2192  {req.Destination}", Style = (Style)Application.Current!.Resources["UcH2"] },
                new Label { Text = $"Driver: {req.DriverName}", Style = (Style)Application.Current!.Resources["UcMutedText"] },
                new Label { Text = req.DepartureLabel, Style = (Style)Application.Current!.Resources["UcTiny"] }
            }
        };

        var statusPill = new Frame
        {
            Padding = new Thickness(10, 4),
            CornerRadius = 999,
            HasShadow = false,
            BackgroundColor = Color.FromArgb(bg),
            Content = new Label { Text = req.Status, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(fg) }
        };

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(textStack, 0);
        grid.Add(statusPill, 1);

        var card = new Border { Style = (Style)Application.Current!.Resources["UcCardSoft"], Content = grid };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Shell.Current.GoToAsync($"{nameof(RideDetailsPage)}?id={req.RideId}");
        card.GestureRecognizers.Add(tap);
        return card;
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
