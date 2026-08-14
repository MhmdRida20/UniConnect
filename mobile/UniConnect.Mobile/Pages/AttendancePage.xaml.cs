using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class AttendancePage : ContentPage
{
    private readonly AttendanceApi _api;
    private readonly SessionStore _session;
    private readonly NotificationsApi _notifications;

    public AttendancePage()
    {
        InitializeComponent();

        _api = ServiceHelper.Get<AttendanceApi>();
        _session = ServiceHelper.Get<SessionStore>();
        _notifications = ServiceHelper.Get<NotificationsApi>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadProfileAsync();
        await LoadUnreadCountAsync();
        await LoadHistoryAsync();
    }

    private async Task LoadProfileAsync()
    {
        var session = await _session.GetAsync();
        ProfileInitials.Text = Avatar.Initials(session?.FullName);
    }

    private async Task LoadUnreadCountAsync()
    {
        var count = await _notifications.GetUnreadCountAsync();
        UnreadBadge.IsVisible = count > 0;
        UnreadBadgeLabel.Text = count > 9 ? "9+" : count.ToString();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadHistoryAsync();
        Refresher.IsRefreshing = false;
    }

    private async Task LoadHistoryAsync()
    {
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        ErrorBox.IsVisible = false;
        EmptyState.IsVisible = false;
        HistoryStack.Children.Clear();

        try
        {
            var records = await _api.GetHistoryAsync();

            RecordCountLabel.Text = $"{records.Count} record{(records.Count == 1 ? "" : "s")}";

            if (records.Count == 0)
            {
                EmptyState.IsVisible = true;
            }
            else
            {
                foreach (var record in records)
                    HistoryStack.Children.Add(BuildHistoryCard(record));
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

    private static Border BuildHistoryCard(AttendanceHistoryEntry record)
    {
        var (pillStyle, textStyle, label) = record.Status switch
        {
            "Present" => ("UcPillGreen", "UcPillText", "Present"),
            "Late" => ("UcPillAmber", "UcPillTextAmber", "Late"),
            "Absent" => ("UcPillRed", "UcPillTextRed", "Absent"),
            _ => ("UcPillGrey", "UcPillTextGrey", record.Status)
        };

        var pill = new Border
        {
            Style = (Style)Application.Current!.Resources[pillStyle]
        };
        pill.Content = new Label
        {
            Text = label,
            Style = (Style)Application.Current!.Resources[textStyle]
        };

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = record.CourseName, Style = (Style)Application.Current!.Resources["UcH2"] },
                new Label { Text = record.DateLabel, Style = (Style)Application.Current!.Resources["UcTiny"] }
            }
        }, 0);
        grid.Add(pill, 1);

        return new Border { Style = (Style)Application.Current!.Resources["UcCardSoft"], Content = grid };
    }

    private async void OnCheckInClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(AttendanceCheckInPage));

    private async void OnNotificationsClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(NotificationsPage));

    private async void OnGroupsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//groups");

    private async void OnInternshipsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//internships");

    private async void OnComingSoonTapped(object? sender, TappedEventArgs e)
    {
        var section = e.Parameter as string ?? "This section";
        await DisplayAlert(section, $"{section} is not part of the mobile app yet. Use the web portal for now.", "OK");
    }

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
    {
        var session = await _session.GetAsync();
        var name = session?.FullName ?? "Your account";

        var choice = await DisplayActionSheet(name, "Cancel", null, "Sign out");
        if (choice == "Sign out") await SignOutAsync();
    }

    private async Task SignOutAsync()
    {
        await _session.ClearAsync();
        await Shell.Current.GoToAsync("//login");
    }

    private async Task<bool> HandleAuthFailureAsync(ApiException ex)
    {
        if (!ex.IsAuthFailure) return false;

        await _session.ClearAsync();
        await Shell.Current.GoToAsync("//login");
        return true;
    }
}
