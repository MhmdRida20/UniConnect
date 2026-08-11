using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class CreateGroupPage : ContentPage
{
	private const string SelectPrompt = "Select a course";

	private readonly StudyGroupsApi _api;
	private readonly SessionStore _session;

	private List<CourseDto> _courses = new();

	public CreateGroupPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<StudyGroupsApi>();
		_session = ServiceHelper.Get<SessionStore>();

		UpdateDescriptionCount();

		// Inputs stretched the full width of a desktop window are hard to scan,
		// so the form keeps a readable measure however wide the window gets.
		FormColumn.MaximumWidthRequest = Responsive.FormMaxWidth;
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

		if (!int.TryParse(MinEntry.Text, out var min))
		{
			ShowField(MinError, "Enter a number.");
			return;
		}

		if (!int.TryParse(MaxEntry.Text, out var max))
		{
			ShowField(MaxError, "Enter a number.");
			return;
		}

		var request = new CreateGroupRequest
		{
			GroupName = NameEntry.Text.Trim(),
			CourseCode = _courses[CoursePicker.SelectedIndex - 1].CourseCode,
			Description = DescriptionEditor.Text?.Trim(),
			MeetingLocation = LocationEntry.Text?.Trim(),
			MinMembers = min,
			MaxMembers = max
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
			ShowField(MinError, ex.For(nameof(CreateGroupRequest.MinMembers)));
			ShowField(MaxError, ex.For(nameof(CreateGroupRequest.MaxMembers)));

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

	private async void OnCancelClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

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
		yield return MinError;
		yield return MaxError;
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
