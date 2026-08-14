using System.Collections.ObjectModel;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

/// <summary>One counter card. Built in code so a counter for a service the
/// university has not enabled can simply be left out of the list.</summary>
public class HomeStat
{
	public string Icon { get; set; } = string.Empty;
	public string Value { get; set; } = "0";
	public string Label { get; set; } = string.Empty;
}

/// <summary>One service card, as the list renders it.</summary>
public class HomeService
{
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Icon { get; set; } = string.Empty;

	/// <summary>Whether tapping the card opens a screen in this app.</summary>
	public bool IsInApp { get; set; }

	public bool IsWebOnly => !IsInApp;

	/// <summary>The absolute Shell route to open, for the in-app ones.</summary>
	public string? Route { get; set; }
}

public partial class HomePage : ContentPage
{
	private readonly HomeApi _api;
	private readonly ProfileStore _profiles;
	private readonly NotificationsApi _notifications;
	private readonly SessionStore _session;

	private readonly ObservableCollection<HomeStat> _stats = new();
	private readonly ObservableCollection<HomeService> _services = new();

	public HomePage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<HomeApi>();
		_profiles = ServiceHelper.Get<ProfileStore>();
		_notifications = ServiceHelper.Get<NotificationsApi>();
		_session = ServiceHelper.Get<SessionStore>();

		// Redraw when the profile screen saves a new picture.
		_profiles.Changed += () => Dispatcher.Dispatch(() => ApplyProfile(_profiles.Current));

		BindableLayout.SetItemsSource(StatsHost, _stats);
		BindableLayout.SetItemsSource(ServicesHost, _services);

		SizeChanged += OnPageSizeChanged;
	}

	/// <summary>
	/// Keeps the scrolling column bound to the viewport, and capped so it does
	/// not sprawl across a desktop monitor — the Windows build runs in a freely
	/// resizable window.
	/// </summary>
	private void OnPageSizeChanged(object? sender, EventArgs e)
	{
		if (Width <= 0) return;

		Column.WidthRequest = Math.Min(Width, Responsive.ContentMaxWidth);
	}

	// ---- lifecycle ---------------------------------------------------------

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await LoadProfileAsync();
		await RefreshUnreadBadgeAsync();

		// Re-read every time: joining a group or a club on another screen
		// changes the counters shown here.
		await LoadAsync();
	}

	// ---- loading -----------------------------------------------------------

	private async Task LoadAsync()
	{
		Refresher.IsRefreshing = true;
		ErrorBox.IsVisible = false;

		try
		{
			Render(await _api.GetDashboardAsync());
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

	private void Render(DashboardDto dashboard)
	{
		GreetingLabel.Text = Greeting();
		NameLabel.Text = dashboard.FirstName;
		UniversityLabel.Text = dashboard.UniversityName;

		var count = dashboard.EnabledServices.Count;
		ServiceCountPill.IsVisible = count > 0;
		ServiceCountLabel.Text = count == 1 ? "1 service enabled" : $"{count} services enabled";

		ServicesSubtitle.Text = count > 0
			? $"What {dashboard.UniversityName} has switched on for you."
			: "Nothing is switched on for your university yet.";

		BuildStats(dashboard);
		BuildServices(dashboard);
	}

	/// <summary>
	/// From the device clock, which is what makes it match the student's actual
	/// day — the server's hour would be wrong for anyone in another timezone.
	/// </summary>
	private static string Greeting() => DateTime.Now.Hour switch
	{
		< 12 => "GOOD MORNING",
		< 18 => "GOOD AFTERNOON",
		_ => "GOOD EVENING"
	};

	/// <summary>
	/// Courses always shows — enrollment is core, not a service. The other three
	/// only appear when the university has that module on, because "0 rides
	/// taken" reads as a failure rather than as "your university doesn't do
	/// rides".
	/// </summary>
	private void BuildStats(DashboardDto dashboard)
	{
		_stats.Clear();

		if (dashboard.HasService(ServiceCodes.StudyGroups))
			_stats.Add(new HomeStat
			{
				Icon = "ic_users_green.png",
				Value = dashboard.Stats.GroupsJoined.ToString(),
				Label = "Groups joined"
			});

		_stats.Add(new HomeStat
		{
			Icon = "ic_book_green.png",
			Value = dashboard.Stats.CoursesEnrolled.ToString(),
			Label = "Courses enrolled"
		});

		if (dashboard.HasService(ServiceCodes.RideSharing))
			_stats.Add(new HomeStat
			{
				Icon = "ic_car_green.png",
				Value = dashboard.Stats.RidesTaken.ToString(),
				Label = "Rides taken"
			});

		if (dashboard.HasService(ServiceCodes.Clubs))
			_stats.Add(new HomeStat
			{
				Icon = "ic_user_multiple_green.png",
				Value = dashboard.Stats.ClubsJoined.ToString(),
				Label = "Clubs joined"
			});
	}

	private void BuildServices(DashboardDto dashboard)
	{
		_services.Clear();

		// The server returns them in table order. Sorted here so the ones that
		// open in the app come first — a list that leads with three "Web" tags
		// makes the app look emptier than it is.
		var ordered = dashboard.EnabledServices
			.Select(s => new HomeService
			{
				Code = s.Code,
				Name = s.Name,
				Description = s.Description ?? string.Empty,
				Icon = IconFor(s.Code),
				IsInApp = RouteFor(s.Code) is not null,
				Route = RouteFor(s.Code)
			})
			.OrderByDescending(s => s.IsInApp)
			.ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase);

		foreach (var service in ordered) _services.Add(service);

		NoServicesBox.IsVisible = _services.Count == 0;
	}

	/// <summary>
	/// Mapped from the service code, not from the row's IconClass: that column
	/// holds Bootstrap class names ("bi-people") which mean nothing here, and
	/// the codes are the stable contract anyway.
	/// </summary>
	private static string IconFor(string code) => code switch
	{
		ServiceCodes.StudyGroups => "ic_users_green.png",
		ServiceCodes.RideSharing => "ic_car_green.png",
		ServiceCodes.Attendance => "ic_qr_green.png",
		ServiceCodes.Tickets => "ic_ticket_green.png",
		ServiceCodes.Internships => "ic_briefcase_green.png",
		ServiceCodes.Clubs => "ic_flag_green.png",
		_ => "ic_check_badge_green.png"
	};

	/// <summary>
	/// Null for anything the app has not built yet, which is what turns the
	/// card's chevron into a "Web" tag rather than letting it promise a screen
	/// that does not exist.
	/// </summary>
	private static string? RouteFor(string code) => code switch
	{
		ServiceCodes.StudyGroups => "//groups",
		ServiceCodes.Internships => "//internships",

		ServiceCodes.Attendance => "//attendance",

		_ => null
	};

	// ---- account -----------------------------------------------------------

	private async Task LoadProfileAsync() => ApplyProfile(await _profiles.GetAsync());

	private void ApplyProfile(ProfileDto? profile)
	{
		ProfileInitials.Text = profile?.Initials ?? "?";

		ProfilePhoto.Source = profile?.HasPicture == true
			? ImageSource.FromUri(new Uri(profile.ProfilePictureUrl!))
			: null;
		ProfilePhoto.IsVisible = profile?.HasPicture == true;
	}

	/// <summary>
	/// Refreshes the bell's badge. Uses the count endpoint, which does not mark
	/// anything read — fetching the list itself would.
	/// </summary>
	private async Task RefreshUnreadBadgeAsync()
	{
		var count = await _notifications.GetUnreadCountAsync();

		UnreadBadge.IsVisible = count > 0;
		UnreadBadgeLabel.Text = count > 99 ? "99+" : count.ToString();
	}

	// ---- navigation --------------------------------------------------------

	private async void OnServiceTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not Element element || element.BindingContext is not HomeService service) return;

		if (service.Route is not null)
		{
			await Shell.Current.GoToAsync(service.Route);
			return;
		}

		await DisplayAlert(
			service.Name,
			$"{service.Name} is not part of the mobile app yet. Use the web portal for now.",
			"OK");
	}

	private async void OnGroupsTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("//groups");

	private async void OnInternshipsTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("//internships");

	private async void OnAttendanceTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("//attendance");

	private async void OnProfileTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync(nameof(ProfilePage));

	private async void OnNotificationsClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync(nameof(NotificationsPage));

	private async void OnRefreshing(object? sender, EventArgs e)
	{
		await RefreshUnreadBadgeAsync();
		await LoadAsync();
	}

	private async Task<bool> HandleAuthFailureAsync(ApiException ex)
	{
		if (!ex.IsAuthFailure) return false;

		await _session.ClearAsync();
		_profiles.Clear();
		await Shell.Current.GoToAsync("//login");
		return true;
	}
}
