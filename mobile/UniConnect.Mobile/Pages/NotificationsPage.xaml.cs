using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class NotificationsPage : ContentPage
{
	private readonly NotificationsApi _api;
	private readonly SessionStore _session;

	private readonly ObservableCollection<NotificationDto> _notifications = new();

	public NotificationsPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<NotificationsApi>();
		_session = ServiceHelper.Get<SessionStore>();

		BindableLayout.SetItemsSource(CardsHost, _notifications);
		Column.MaximumWidthRequest = Responsive.FormMaxWidth;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadAsync();
	}

	private async Task LoadAsync()
	{
		Refresher.IsRefreshing = true;
		ErrorBox.IsVisible = false;

		try
		{
			// This call marks everything it returns as read, server-side. That
			// is why the page has no "mark all read" button: opening it is the
			// action, exactly as on the web.
			var notifications = await _api.GetAsync();

			_notifications.Clear();
			foreach (var notification in notifications)
				_notifications.Add(notification);

			EmptyState.IsVisible = _notifications.Count == 0;
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;

			ErrorLabel.Text = ex.Message;
			ErrorBox.IsVisible = true;
		}
		finally
		{
			Refresher.IsRefreshing = false;
		}
	}

	private async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

	// ---- following a notification -------------------------------------------

	/// <summary>
	/// Notification links are web paths, written for the browser. The ones whose
	/// destination also exists in the app are translated to its routes; the rest
	/// say so rather than opening a page the app cannot render.
	/// </summary>
	private async void OnNotificationTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not Element element || element.BindingContext is not NotificationDto notification) return;
		if (!notification.HasLink) return;

		var route = ToAppRoute(notification.Link!);

		if (route is null)
		{
			await DisplayAlert(
				notification.Title,
				$"{notification.Message}\n\nThis one opens on the web portal — the mobile app does not have that screen yet.",
				"OK");
			return;
		}

		await Shell.Current.GoToAsync(route);
	}

	/// <summary>
	/// Maps a web link to an app route, or null when the app has no equivalent.
	///
	/// Only Study Groups and Internships are covered because they are the only
	/// features the app has. Clubs, Rides and the company screens stay web-only,
	/// and returning null for them is what produces the honest message above.
	/// </summary>
	private static string? ToAppRoute(string link)
	{
		var path = link.Trim();

		var groupDetails = Regex.Match(path, @"^/StudyGroups/Details/(\d+)", RegexOptions.IgnoreCase);
		if (groupDetails.Success)
			return $"//groups/{nameof(GroupDetailsPage)}?id={groupDetails.Groups[1].Value}";

		if (path.StartsWith("/StudyGroups", StringComparison.OrdinalIgnoreCase))
			return "//groups";

		if (path.StartsWith("/Internships/MyApplications", StringComparison.OrdinalIgnoreCase))
			return $"//internships/{nameof(MyApplicationsPage)}";

		var internshipDetails = Regex.Match(path, @"^/Internships/Details/(\d+)", RegexOptions.IgnoreCase);
		if (internshipDetails.Success)
			return $"//internships/{nameof(InternshipDetailsPage)}?id={internshipDetails.Groups[1].Value}";

		if (path.StartsWith("/Internships", StringComparison.OrdinalIgnoreCase))
			return "//internships";

		return null;
	}

	private async void OnBackTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");

	private async Task<bool> HandleAuthFailureAsync(ApiException ex)
	{
		if (!ex.IsAuthFailure) return false;

		await _session.ClearAsync();
		await Shell.Current.GoToAsync("//login");
		return true;
	}
}
