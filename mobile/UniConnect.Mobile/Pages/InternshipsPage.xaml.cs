using System.Collections.ObjectModel;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class InternshipsPage : ContentPage
{
	private readonly InternshipsApi _api;
	private readonly SessionStore _session;
	private readonly NotificationsApi _notifications;
	private readonly ProfileStore _profiles;
	private readonly StudyGroupHubClient _hub;

	/// <summary>What the list shows: the loaded listings after the search filter.</summary>
	private readonly ObservableCollection<InternshipSummary> _visible = new();

	/// <summary>Everything the server returned for the current server-side filters.</summary>
	private List<InternshipSummary> _all = new();

	/// <summary>
	/// Duration options, as (label, max weeks). Null means no cap. The server
	/// treats the value as "this long or shorter".
	/// </summary>
	private static readonly (string Label, int? MaxWeeks)[] Durations =
	{
		("Any duration", null),
		("Up to 4 weeks", 4),
		("Up to 8 weeks", 8),
		("Up to 12 weeks", 12),
		("Up to 24 weeks", 24)
	};

	/// <summary>
	/// Suppresses the filter handlers while the page fills the controls in, so
	/// setting SelectedIndex during load does not fire a second fetch.
	/// </summary>
	private bool _loading;

	/// <summary>Whether the "My major" chip is on. Replaces the old CheckBox.</summary>
	private bool _myMajorOnly;

	public InternshipsPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<InternshipsApi>();
		_session = ServiceHelper.Get<SessionStore>();
		_notifications = ServiceHelper.Get<NotificationsApi>();
		_profiles = ServiceHelper.Get<ProfileStore>();

		// Redraw when the profile screen saves a new picture.
		_profiles.Changed += () => Dispatcher.Dispatch(() => ApplyProfile(_profiles.Current));
		_hub = ServiceHelper.Get<StudyGroupHubClient>();

		BindableLayout.SetItemsSource(CardsHost, _visible);

		_loading = true;
		DurationPicker.ItemsSource = Durations.Select(d => d.Label).ToList();
		DurationPicker.SelectedIndex = 0;
		DurationLabel.Text = Durations[0].Label;
		_loading = false;

		SizeChanged += OnPageSizeChanged;
	}

	private void OnPageSizeChanged(object? sender, EventArgs e)
	{
		if (Width <= 0) return;

		// Same reasoning as GroupsPage: children that measure wider than the
		// viewport would drag the whole scrolling column with them.
		var contentWidth = Math.Min(Width, Responsive.ContentMaxWidth);
		HeaderColumn.WidthRequest = contentWidth;

		// Both chips get exactly half the row. Sizing them here rather than
		// letting the Picker ask is what stops "Any duration" being truncated:
		// a Picker's desired width is whatever its longest item needs, which is
		// never the width it actually has.
		var chipWidth = (contentWidth - (PageGutter * 2) - ChipGap) / 2;
		DurationChip.WidthRequest = chipWidth;
		MajorChip.WidthRequest = chipWidth;
	}

	/// <summary>Page side margin.</summary>
	private const double PageGutter = 20;

	/// <summary>Space between the two filter chips.</summary>
	private const double ChipGap = 12;

	/// <summary>
	/// A focus ring in brand green, which is the only affordance telling a
	/// student the field is live — the frame is otherwise the same hairline as
	/// every other surface.
	/// </summary>
	private void OnSearchFocusChanged(object? sender, FocusEventArgs e)
	{
		SearchFrame.Stroke = e.IsFocused
			? (Color)Application.Current!.Resources["UcGreen"]
			: (Color)Application.Current!.Resources["UcBorder"];

		SearchFrame.StrokeThickness = e.IsFocused ? 2 : 1;
	}

	/// <summary>
	/// The Picker is invisible and only owns the list; this keeps the visible
	/// label in step with it.
	/// </summary>
	private async void OnDurationChanged(object? sender, EventArgs e)
	{
		var index = Math.Clamp(DurationPicker.SelectedIndex, 0, Durations.Length - 1);
		DurationLabel.Text = Durations[index].Label;

		if (_loading) return;
		await LoadAsync();
	}

	private async void OnMyMajorTapped(object? sender, TappedEventArgs e)
	{
		_myMajorOnly = !_myMajorOnly;

		MajorCheckIcon.Source = _myMajorOnly ? "ic_check_badge_green.png" : "ic_check_badge_muted.png";
		MajorChip.Stroke = _myMajorOnly
			? (Color)Application.Current!.Resources["UcGreen"]
			: (Color)Application.Current!.Resources["UcBorder"];
		MajorChip.BackgroundColor = _myMajorOnly
			? (Color)Application.Current!.Resources["UcMint"]
			: (Color)Application.Current!.Resources["UcSurface"];

		await LoadAsync();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await LoadProfileAsync();

		// Re-read every time: applying on the details page changes the badge
		// shown on the card here.
		await RefreshUnreadBadgeAsync();
		await LoadAsync();
	}

	// ---- loading -----------------------------------------------------------

	private async Task LoadAsync()
	{
		Refresher.IsRefreshing = true;
		ErrorBox.IsVisible = false;

		try
		{
			_all = await _api.GetInternshipsAsync(
				maxDuration: Durations[Math.Max(0, DurationPicker.SelectedIndex)].MaxWeeks,
				myMajorOnly: _myMajorOnly);

			ApplySearch();

			// The server decides matching; this is only the client's read of
			// "nothing here fits you well", used to point at the career profile.
			ProfileHint.IsVisible = _all.Count > 0 && _all.Max(i => i.MatchingScore) < 20;
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;

			ErrorLabel.Text = ex.Message;
			ErrorBox.IsVisible = true;

			_all = new List<InternshipSummary>();
			ApplySearch();
		}
		finally
		{
			Refresher.IsRefreshing = false;
		}
	}

	// ---- filtering ---------------------------------------------------------

	/// <summary>
	/// Local. The duration and major filters re-query because they change which
	/// listings exist for the student; typing only narrows what is already here.
	/// </summary>
	private void ApplySearch()
	{
		var term = SearchInput.Text?.Trim().ToLowerInvariant() ?? string.Empty;

		var matches = term.Length == 0
			? _all
			: _all.Where(i => i.SearchKey.Contains(term)).ToList();

		_visible.Clear();
		foreach (var internship in matches)
			_visible.Add(internship);

		var searching = term.Length > 0;
		EmptyTitle.Text = searching ? "No matches" : "No internships listed";
		EmptyBody.Text = searching
			? "No internships match your search. Try a different keyword."
			: "There are no open internships for your university right now.";

		EmptyState.IsVisible = _visible.Count == 0;

		ResultsLabel.Text = _all.Count == 0
			? string.Empty
			: $"Showing {_visible.Count} of {_all.Count} internship{(_all.Count == 1 ? "" : "s")}";
	}

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplySearch();

	private async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

	// ---- navigation --------------------------------------------------------

	private async void OnCardTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not Element element || element.BindingContext is not InternshipSummary internship) return;

		await Shell.Current.GoToAsync($"{nameof(InternshipDetailsPage)}?id={internship.Id}");
	}

	private async void OnMyApplicationsClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync(nameof(MyApplicationsPage));

	/// <summary>
	/// The avatar opens the profile screen, which is where sign out lives. It
	/// used to raise a native action sheet, whose styling the app cannot reach
	/// and which looked nothing like the rest of it.
	/// </summary>
	private async void OnProfileTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync(nameof(ProfilePage));

	/// <summary>
	/// Draws the account avatar from the shared store, so a picture changed on
	/// the profile screen shows here without this page re-fetching anything.
	/// </summary>
	private async Task LoadProfileAsync()
	{
		var profile = await _profiles.GetAsync();
		ApplyProfile(profile);
	}

	private void ApplyProfile(ProfileDto? profile)
	{
		ProfileInitials.Text = profile?.Initials ?? "?";

		ProfilePhoto.Source = profile?.HasPicture == true
			? ImageSource.FromUri(new Uri(profile.ProfilePictureUrl!))
			: null;
		ProfilePhoto.IsVisible = profile?.HasPicture == true;
	}

	private async void OnNotificationsClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync(nameof(NotificationsPage));

	/// <summary>
	/// Refreshes the bell's badge. The count endpoint is used rather than the
	/// list, because fetching the list marks everything read.
	/// </summary>
	private async Task RefreshUnreadBadgeAsync()
	{
		var count = await _notifications.GetUnreadCountAsync();

		UnreadBadge.IsVisible = count > 0;
		UnreadBadgeLabel.Text = count > 99 ? "99+" : count.ToString();
	}

	private async void OnGroupsTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("//groups");

	private async void OnHomeTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("//home");

	private async void OnAttendanceTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("//attendance");

	private async Task<bool> HandleAuthFailureAsync(ApiException ex)
	{
		if (!ex.IsAuthFailure) return false;

		await _session.ClearAsync();
		await Shell.Current.GoToAsync("//login");
		return true;
	}
}
