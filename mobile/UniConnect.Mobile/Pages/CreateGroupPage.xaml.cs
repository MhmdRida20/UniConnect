using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class CreateGroupPage : ContentPage
{
	private const string SelectPrompt = "Choose a registered course";

	// Capacity bounds. The university's own cap is enforced server-side and can
	// be lower than MaxCapacity — the stepper only keeps the value sane locally.
	private const int MinCapacity = 2;
	private const int MaxCapacity = 50;
	private const int DefaultCapacity = 6;

	/// <summary>
	/// The floor on members, which this form does not ask for.
	///
	/// The API needs a MinMembers and the web form exposes it, but the design
	/// for this screen has a single "Capacity" and adding a second stepper for a
	/// number most students would leave alone is not worth the space. It is sent
	/// as 2, clamped so it can never exceed the chosen capacity.
	/// </summary>
	private const int DefaultMinMembers = 2;

	private int _capacity = DefaultCapacity;

	private readonly StudyGroupsApi _api;
	private readonly SessionStore _session;

	private List<CourseDto> _courses = new();

	public CreateGroupPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<StudyGroupsApi>();
		_session = ServiceHelper.Get<SessionStore>();

		UpdateDescriptionCount();
		UpdateCapacity();

		// Inputs stretched the full width of a desktop window are hard to scan,
		// so the form keeps a readable measure however wide the window gets.
		FormColumn.MaximumWidthRequest = Responsive.FormMaxWidth;
		ActionColumn.MaximumWidthRequest = Responsive.FormMaxWidth;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_courses.Count == 0) await LoadCoursesAsync();
	}

	private async Task LoadCoursesAsync()
	{
		try
		{
			_courses = await _api.GetMyCoursesAsync();

			// Item 0 is the prompt, so a course is only chosen once the
			// selection moves past it.
			var options = new List<string> { SelectPrompt };
			options.AddRange(_courses.Select(c => c.Display));
			CoursePicker.ItemsSource = options;
			CoursePicker.SelectedIndex = 0;

			if (_courses.Count == 0)
				ShowSummary("You are not enrolled in any courses yet, so there is nothing to create a group for.");
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;
			ShowSummary(ex.Message);
		}
	}

	// ---- capacity stepper -------------------------------------------------

	private void OnCapacityDown(object? sender, TappedEventArgs e) => StepCapacity(-1);

	private void OnCapacityUp(object? sender, TappedEventArgs e) => StepCapacity(1);

	private void StepCapacity(int delta)
	{
		var next = Math.Clamp(_capacity + delta, MinCapacity, MaxCapacity);
		if (next == _capacity) return;

		_capacity = next;
		UpdateCapacity();
	}

	private void UpdateCapacity()
	{
		CapacityLabel.Text = _capacity.ToString();

		// Dimmed rather than hidden, so the stepper keeps its shape at the ends
		// of the range instead of the row shifting under the student's thumb.
		CapacityDown.Opacity = _capacity > MinCapacity ? 1 : 0.4;
		CapacityUp.Opacity = _capacity < MaxCapacity ? 1 : 0.4;
	}

	private void OnDescriptionChanged(object? sender, TextChangedEventArgs e) => UpdateDescriptionCount();

	private void UpdateDescriptionCount() =>
		DescriptionCount.Text = $"{DescriptionEditor.Text?.Length ?? 0} / 500";

	private async void OnCreateClicked(object? sender, EventArgs e)
	{
		ClearErrors();

		// Only the checks that cost nothing locally — a tapped-through form
		// shouldn't need a round trip to learn it is empty. Everything else,
		// including the university's member cap and enrolment, is the server's
		// to answer.
		if (string.IsNullOrWhiteSpace(NameEntry.Text))
		{
			ShowField(NameError, "Group name is required.");
			return;
		}

		// Index 0 is the "Select a course" prompt, not a course.
		if (CoursePicker.SelectedIndex <= 0)
		{
			ShowField(CourseError, "Please choose a course.");
			return;
		}

		var request = new CreateGroupRequest
		{
			GroupName = NameEntry.Text.Trim(),
			CourseCode = _courses[CoursePicker.SelectedIndex - 1].CourseCode,
			Description = DescriptionEditor.Text?.Trim(),
			MeetingLocation = LocationEntry.Text?.Trim(),
			MinMembers = Math.Min(DefaultMinMembers, _capacity),
			MaxMembers = _capacity
		};

		SetBusy(true);
		try
		{
			var group = await _api.CreateAsync(request);

			// Straight into the new group, as the web does after a successful
			// create — and the list refreshes itself on the way back.
			await Shell.Current.GoToAsync($"../{nameof(GroupDetailsPage)}?id={group.Id}");
		}
		catch (FieldValidationException ex)
		{
			// Each server message lands under the input it belongs to. Field
			// names come back matching the DTO's property names.
			ShowField(NameError, ex.For(nameof(CreateGroupRequest.GroupName)));
			ShowField(CourseError, ex.For(nameof(CreateGroupRequest.CourseCode)));
			ShowField(DescriptionError, ex.For(nameof(CreateGroupRequest.Description)));
			ShowField(LocationError, ex.For(nameof(CreateGroupRequest.MeetingLocation)));
			// Both bounds are set by the one stepper, so either complaint belongs
			// under it.
			ShowField(CapacityError, ex.For(nameof(CreateGroupRequest.MaxMembers))
				?? ex.For(nameof(CreateGroupRequest.MinMembers)));

			// Anything the server flagged against a field this form doesn't
			// show would otherwise vanish silently.
			if (!AnyFieldErrorVisible()) ShowSummary(ex.Message);
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;
			ShowSummary(ex.Message);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async void OnCancelTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");

	// ---- error display ----------------------------------------------------

	private void ClearErrors()
	{
		foreach (var label in ErrorLabels()) label.IsVisible = false;
		SummaryErrorBox.IsVisible = false;
	}

	private IEnumerable<Label> ErrorLabels()
	{
		yield return NameError;
		yield return CourseError;
		yield return DescriptionError;
		yield return LocationError;
		yield return CapacityError;
	}

	private bool AnyFieldErrorVisible() => ErrorLabels().Any(l => l.IsVisible);

	private static void ShowField(Label label, string? message)
	{
		if (string.IsNullOrEmpty(message)) return;

		label.Text = message;
		label.IsVisible = true;
	}

	private void ShowSummary(string message)
	{
		SummaryError.Text = message;
		SummaryErrorBox.IsVisible = true;
	}

	private void SetBusy(bool busy)
	{
		Busy.IsRunning = busy;
		Busy.IsVisible = busy;
		SubmitBtn.IsEnabled = !busy;
	}

	private async Task<bool> HandleAuthFailureAsync(ApiException ex)
	{
		if (!ex.IsAuthFailure) return false;

		await _session.ClearAsync();
		await Shell.Current.GoToAsync("//login");
		return true;
	}
}
