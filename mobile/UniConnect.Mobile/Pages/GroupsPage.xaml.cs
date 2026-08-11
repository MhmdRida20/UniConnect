using System.Collections.ObjectModel;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class GroupsPage : ContentPage
{
	private const string AllCourses = "All my courses";

	private readonly StudyGroupsApi _api;
	private readonly SessionStore _session;
	private readonly StudyGroupHubClient _hub;

	/// <summary>What the list shows: the loaded groups after the search filter.</summary>
	private readonly ObservableCollection<GroupSummary> _visible = new();

	/// <summary>Everything the server returned for the current course filter.</summary>
	private List<GroupSummary> _all = new();

	private List<CourseDto> _courses = new();
	private bool _coursesLoaded;
	private bool _subscribed;
	private int _columns = 1;

	public GroupsPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<StudyGroupsApi>();
		_session = ServiceHelper.Get<SessionStore>();
		_hub = ServiceHelper.Get<StudyGroupHubClient>();

		GroupsView.ItemsSource = _visible;

		SizeChanged += OnPageSizeChanged;
	}

	// ---- responsive layout -------------------------------------------------

	/// <summary>
	/// Reflows the card grid at the same widths the web does, so a desktop
	/// window shows two or three columns instead of one tall ribbon.
	/// </summary>
	private void OnPageSizeChanged(object? sender, EventArgs e)
	{
		if (Width <= 0) return;

		var columns = Responsive.CardColumns(Width);
		if (columns != _columns)
		{
			_columns = columns;
			GroupsView.ItemsLayout = new GridItemsLayout(columns, ItemsLayoutOrientation.Vertical);
		}

		// Stop the controls row and hero from sprawling on a wide monitor.
		ControlsColumn.MaximumWidthRequest = Responsive.ContentMaxWidth;
		GroupsView.MaximumWidthRequest = Responsive.ContentMaxWidth;

		// The hero's action button drops under the title when there is no room
		// for it beside one, and the course filter drops under the search box
		// rather than squeezing it to a few characters.
		var narrow = Width < Responsive.Sm;

		HeroGrid.ColumnDefinitions[1].Width = narrow ? 0 : GridLength.Auto;
		CreateBtn.IsVisible = !narrow;
		NarrowCreateBtn.IsVisible = narrow;

		Grid.SetRow(FilterFrame, narrow ? 1 : 0);
		Grid.SetColumn(FilterFrame, narrow ? 0 : 1);
		Grid.SetColumnSpan(FilterFrame, narrow ? 2 : 1);
		CoursePicker.WidthRequest = narrow ? -1 : 165;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

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
			ApplySearch();
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

	private string? SelectedCourseCode() =>
		CoursePicker.SelectedIndex <= 0 || CoursePicker.SelectedIndex > _courses.Count
			? null
			: _courses[CoursePicker.SelectedIndex - 1].CourseCode;

	// ---- filtering --------------------------------------------------------

	/// <summary>
	/// Local, like the web's search box: the course filter is a server query
	/// because it decides which groups exist for you, but typing just narrows
	/// what has already been fetched.
	/// </summary>
	private void ApplySearch()
	{
		var term = SearchInput.Text?.Trim().ToLowerInvariant() ?? string.Empty;

		var matches = term.Length == 0
			? _all
			: _all.Where(g => g.SearchKey.Contains(term)).ToList();

		_visible.Clear();
		foreach (var group in matches)
			_visible.Add(group);

		var searching = term.Length > 0;
		EmptyTitle.Text = searching ? "No matches" : "No study groups yet";
		EmptyBody.Text = searching
			? "No groups match your search. Try a different keyword."
			: "There are no groups for your courses right now. Be the first to start one!";

		ResultsLabel.Text = _all.Count == 0
			? string.Empty
			: $"Showing {_visible.Count} of {_all.Count} group{(_all.Count == 1 ? "" : "s")}";
	}

	private void UpdateCounts()
	{
		// Mirrors the web hero: total · open · yours.
		var open = _all.Count(g => !g.IsFull);
		var mine = _all.Count(g => g.AmMember);

		HeroSubLabel.Text =
			$"{_all.Count} group{(_all.Count == 1 ? "" : "s")} for your courses · {open} open · {mine} you're in";
	}

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplySearch();

	private async void OnCourseFilterChanged(object? sender, EventArgs e)
	{
		if (!_coursesLoaded) return;
		await LoadGroupsAsync();
	}

	// ---- navigation -------------------------------------------------------

	private async void OnRefreshing(object? sender, EventArgs e) => await LoadGroupsAsync();

	private async void OnCreateClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync(nameof(CreateGroupPage));

	private async void OnGroupSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is not GroupSummary group) return;

		// Clear immediately, otherwise the row stays highlighted and tapping it
		// again after coming back raises nothing.
		GroupsView.SelectedItem = null;

		await Shell.Current.GoToAsync($"{nameof(GroupDetailsPage)}?id={group.Id}");
	}

	private async void OnSignOutClicked(object? sender, EventArgs e)
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
