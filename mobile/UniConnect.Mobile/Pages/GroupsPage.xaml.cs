using System.Collections.ObjectModel;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class GroupsPage : ContentPage
{
	private readonly StudyGroupsApi _api;
	private readonly SessionStore _session;
	private readonly StudyGroupHubClient _hub;
	private readonly NotificationsApi _notifications;

	/// <summary>What the list shows: the loaded groups after the search filter.</summary>
	private readonly ObservableCollection<GroupSummary> _visible = new();

	/// <summary>Everything the server returned for the current course filter.</summary>
	private List<GroupSummary> _all = new();

	private List<CourseDto> _courses = new();
	private bool _coursesLoaded;
	private bool _subscribed;

	private const string AllCourses = "All my courses";

	/// <summary>
	/// What the chip row filters on. The course dropdown decides which groups
	/// the server returns; this then narrows what is shown of them, so the two
	/// controls do different jobs rather than duplicating one.
	/// </summary>
	private enum Scope { All, Open, Joined, Full }

	private Scope _scope = Scope.All;

	public GroupsPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<StudyGroupsApi>();
		_session = ServiceHelper.Get<SessionStore>();
		_hub = ServiceHelper.Get<StudyGroupHubClient>();
		_notifications = ServiceHelper.Get<NotificationsApi>();

		BindableLayout.SetItemsSource(CardsHost, _visible);
		BuildChips();

		SizeChanged += OnPageSizeChanged;
	}

	/// <summary>
	/// Fills the account avatar. Done here rather than in the constructor
	/// because reading the session is async, and the initials should refresh if
	/// a different student signs in without the process restarting.
	/// </summary>
	private async Task LoadProfileAsync()
	{
		var session = await _session.GetAsync();
		ProfileInitials.Text = Avatar.Initials(session?.FullName);
	}

	// ---- responsive layout -------------------------------------------------

	/// <summary>
	/// Keeps the scrolling column bound to the viewport. Several children would
	/// otherwise measure wider than the screen and drag the whole column with
	/// them — see the notes on each below.
	/// </summary>
	private void OnPageSizeChanged(object? sender, EventArgs e)
	{
		if (Width <= 0) return;

		// Capped so the column does not sprawl across a desktop monitor.
		var contentWidth = Math.Min(Width, Responsive.ContentMaxWidth);
		HeaderColumn.WidthRequest = contentWidth;

		var innerWidth = contentWidth - (PageGutter * 2);

		CoursePicker.WidthRequest = innerWidth - (InputPadding * 2);

		// Same reason as the Picker: a horizontal ScrollView reports its whole
		// content as its desired width, so the chip row would widen the header
		// past the viewport and take every card with it.
		ChipScroller.WidthRequest = innerWidth;
	}

	/// <summary>Side margin on the page, per the design system's 24px rhythm.</summary>
	private const double PageGutter = 24;

	/// <summary>Horizontal padding inside an input frame.</summary>
	private const double InputPadding = 16;

	// ---- scope chips -------------------------------------------------------

	private void BuildChips()
	{
		ChipRow.Clear();

		ChipRow.Add(Chip("All Groups", Scope.All));
		ChipRow.Add(Chip("Open", Scope.Open));
		ChipRow.Add(Chip("Joined", Scope.Joined));
		ChipRow.Add(Chip("Full", Scope.Full));
	}

	private Border Chip(string text, Scope scope)
	{
		var active = _scope == scope;

		var label = new Label { Text = text, Style = Theme(active ? "UcChipTextActive" : "UcChipText") };
		var chip = new Border { Content = label, Style = Theme(active ? "UcChipActive" : "UcChip") };

		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => SelectScope(scope);
		chip.GestureRecognizers.Add(tap);

		return chip;
	}

	/// <summary>
	/// Local, so switching scope is instant. Nothing here changes which groups
	/// exist for the student — only which of the loaded ones are listed.
	/// </summary>
	private void SelectScope(Scope scope)
	{
		if (_scope == scope) return;

		_scope = scope;
		BuildChips();
		ApplyFilters();
	}

	/// <summary>
	/// Looks a style up in the app-level dictionary. A page's own Resources do
	/// not cascade upwards, so indexing this.Resources would miss everything in
	/// UniConnect.xaml.
	/// </summary>
	private static Style Theme(string key) =>
		(Style)Application.Current!.Resources[key];

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await LoadProfileAsync();
		await RefreshUnreadBadgeAsync();

		// The course list rarely changes, so it is fetched once; the groups are
		// re-read every time because joining one on the details page changes
		// the member counts shown here.
		if (!_coursesLoaded) await LoadCoursesAsync();

		await LoadGroupsAsync();

		// The lobby pushes "something changed" for any group, so a group
		// created in a browser shows up here without a manual refresh.
		if (!_subscribed)
		{
			_subscribed = true;
			_hub.ListChanged += OnListChanged;
			await _hub.JoinLobbyAsync();
		}
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();

		// Leaving the lobby while a details page is on top costs nothing:
		// OnAppearing re-joins and reloads the list on the way back, so no
		// update can be missed.
		if (!_subscribed) return;
		_subscribed = false;

		_hub.ListChanged -= OnListChanged;
		await _hub.LeaveLobbyAsync();
	}

	private void OnListChanged() =>
		Dispatcher.Dispatch(() => UpdateBanner.IsVisible = true);

	private async void OnBannerRefreshClicked(object? sender, EventArgs e)
	{
		UpdateBanner.IsVisible = false;
		await LoadGroupsAsync();
	}

	// ---- loading ----------------------------------------------------------

	private async Task LoadCoursesAsync()
	{
		try
		{
			_courses = await _api.GetMyCoursesAsync();

			var titles = new List<string> { AllCourses };
			titles.AddRange(_courses.Select(c => c.Display));

			CoursePicker.ItemsSource = titles;
			CoursePicker.SelectedIndex = 0;

			_coursesLoaded = true;
		}
		catch (ApiException ex)
		{
			// A failed course list should not stop the groups from loading —
			// it only costs the filter.
			if (await HandleAuthFailureAsync(ex)) return;
		}
	}

	private async Task LoadGroupsAsync()
	{
		Refresher.IsRefreshing = true;
		ErrorBox.IsVisible = false;

		try
		{
			_all = await _api.GetGroupsAsync(SelectedCourseCode());
			ApplyFilters();
			UpdateCounts();
			UpdateBanner.IsVisible = false;
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

	// ---- filtering --------------------------------------------------------

	/// <summary>
	/// Search and scope, both local: the course dropdown is a server query
	/// because it decides which groups exist for you, but typing and switching
	/// scope only narrow what has already been fetched.
	/// </summary>
	private void ApplyFilters()
	{
		var term = SearchInput.Text?.Trim().ToLowerInvariant() ?? string.Empty;

		var matches = _all.Where(g => _scope switch
		{
			Scope.Open => !g.IsFull && !g.AmMember,
			Scope.Joined => g.AmMember,
			Scope.Full => g.IsFull,
			_ => true
		});

		if (term.Length > 0)
			matches = matches.Where(g => g.SearchKey.Contains(term));

		_visible.Clear();
		foreach (var group in matches)
			_visible.Add(group);

		var narrowed = term.Length > 0 || _scope != Scope.All;
		EmptyTitle.Text = narrowed ? "No matches" : "No study groups yet";
		EmptyBody.Text = narrowed
			? "No groups match this filter. Try a different keyword or scope."
			: "There are no groups for your courses right now. Be the first to start one!";

		EmptyState.IsVisible = _visible.Count == 0;

		ResultsLabel.Text = _all.Count == 0
			? string.Empty
			: $"Showing {_visible.Count} of {_all.Count} group{(_all.Count == 1 ? "" : "s")}";
	}

	/// <summary>
	/// The subtitle carries the numbers the hero used to: how many groups are
	/// on offer, how many still have room, and how many you are already in.
	/// </summary>
	private void UpdateCounts()
	{
		if (_all.Count == 0)
		{
			SubtitleLabel.Text = "Find a group or create your own.";
			return;
		}

		var open = _all.Count(g => !g.IsFull);
		var mine = _all.Count(g => g.AmMember);

		SubtitleLabel.Text =
			$"{_all.Count} group{(_all.Count == 1 ? "" : "s")} · {open} open · {mine} you're in";
	}

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilters();

	private string? SelectedCourseCode() =>
		CoursePicker.SelectedIndex <= 0 || CoursePicker.SelectedIndex > _courses.Count
			? null
			: _courses[CoursePicker.SelectedIndex - 1].CourseCode;

	private async void OnCourseFilterChanged(object? sender, EventArgs e)
	{
		if (!_coursesLoaded) return;
		await LoadGroupsAsync();
	}

	/// <summary>
	/// The avatar is the account menu. Sign out lives here rather than as its
	/// own button, which is what leaves the top bar with room for the brand.
	/// </summary>
	private async void OnProfileTapped(object? sender, TappedEventArgs e)
	{
		var session = await _session.GetAsync();
		var name = session?.FullName ?? "Your account";

		var choice = await DisplayActionSheet(name, "Cancel", null, "Sign out");
		if (choice == "Sign out") await SignOutAsync();
	}

	private async void OnNotificationsClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync(nameof(NotificationsPage));

	/// <summary>
	/// Refreshes the bell's badge. Uses the count endpoint, which does not mark
	/// anything read — fetching the list itself would, so checking the badge
	/// would clear the very thing it reports.
	/// </summary>
	private async Task RefreshUnreadBadgeAsync()
	{
		var count = await _notifications.GetUnreadCountAsync();

		UnreadBadge.IsVisible = count > 0;
		UnreadBadgeLabel.Text = count > 99 ? "99+" : count.ToString();
	}

	/// <summary>
	/// The tab bar shows the app's planned shape, but Study Groups is the only
	/// section built. Saying so beats opening an empty screen.
	/// </summary>
	private async void OnInternshipsTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("//internships");

	private async void OnAttendanceTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("//attendance");

	private async void OnComingSoonTapped(object? sender, TappedEventArgs e)
	{
		var section = e.Parameter as string ?? "This section";
		await DisplayAlert(section, $"{section} is not part of the mobile app yet. Use the web portal for now.", "OK");
	}

	// ---- navigation -------------------------------------------------------

	private async void OnRefreshing(object? sender, EventArgs e) => await LoadGroupsAsync();

	private async void OnCreateClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync(nameof(CreateGroupPage));

	private async void OnCardTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not Element element || element.BindingContext is not GroupSummary group) return;

		await Shell.Current.GoToAsync($"{nameof(GroupDetailsPage)}?id={group.Id}");
	}

	private async Task SignOutAsync()
	{
		// Drop the hub connection too — it is authenticated as the outgoing
		// student and would otherwise keep receiving their groups' traffic.
		await _hub.StopAsync();
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
