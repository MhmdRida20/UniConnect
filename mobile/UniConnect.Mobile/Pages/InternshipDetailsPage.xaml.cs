using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

/// <summary>
/// One internship, with the apply form when the server says applying is on
/// offer. The id arrives as a query string, so the property is named to avoid
/// hiding Element.Id.
/// </summary>
[QueryProperty(nameof(InternshipIdParameter), "id")]
public partial class InternshipDetailsPage : ContentPage
{
	private readonly InternshipsApi _api;
	private readonly SessionStore _session;

	private InternshipDetail? _detail;

	public InternshipDetailsPage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<InternshipsApi>();
		_session = ServiceHelper.Get<SessionStore>();

		Column.MaximumWidthRequest = Responsive.FormMaxWidth;
	}

	private int _internshipId;

	public string InternshipIdParameter
	{
		set => _internshipId = int.TryParse(value, out var id) ? id : 0;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadAsync();
	}

	private async Task LoadAsync()
	{
		SetBusy(true);
		ErrorBox.IsVisible = false;

		try
		{
			_detail = await _api.GetInternshipAsync(_internshipId);
			Render(_detail);
			Body.IsVisible = true;
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;

			ErrorLabel.Text = ex.Message;
			ErrorBox.IsVisible = true;
			Body.IsVisible = false;
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void Render(InternshipDetail d)
	{
		TitleLabel.Text = d.Title;
		CompanyLabel.Text = d.CompanyName;
		MatchPill.Text = d.MatchLabel;
		PartialScoreNote.IsVisible = d.ScoreIsPartial;
		ExternalPill.IsVisible = d.IsExternal;

		LocationLabel.Text = string.IsNullOrWhiteSpace(d.Location) ? "Location not stated" : d.Location;
		DurationLabel.Text = d.DurationLabel;
		DeadlineLabel.Text = d.DeadlineLabel;
		PositionsLabel.Text = d.PositionsLabel;

		DescriptionLabel.Text = d.Description;
		DescriptionCard.IsVisible = d.HasDescription;

		CoursesLabel.Text = d.RecommendedCourses;
		CoursesCard.IsVisible = d.HasRecommendedCourses;

		RenderSkills(d);

		// Exactly one of these shows. CanApply comes from the server and mirrors
		// what the apply endpoint will accept.
		ApplyCard.IsVisible = d.CanApply;
		CannotApplyCard.IsVisible = !d.CanApply;

		CvHint.IsVisible = d.ShouldPromptForCv;
		CannotApplyLabel.Text = d.CannotApplyReason;

		// An external listing is the one refusal with somewhere else to go.
		ExternalApplyBtn.IsVisible = d.IsExternal && (d.HasExternalUrl || d.HasExternalEmail);
	}

	private void RenderSkills(InternshipDetail d)
	{
		SkillsHost.Clear();

		foreach (var skill in d.SkillList)
		{
			SkillsHost.Add(new Border
			{
				Style = Theme("UcPillMint"),
				Margin = new Thickness(0, 0, 8, 8),
				Content = new Label { Text = skill, Style = Theme("UcPillTextMint") }
			});
		}

		SkillsCard.IsVisible = d.SkillList.Count > 0;
	}

	// ---- actions -----------------------------------------------------------

	private async void OnApplyClicked(object? sender, EventArgs e)
	{
		if (_detail is null) return;

		SetBusy(true);
		try
		{
			var message = await _api.ApplyAsync(_internshipId, CoverEditor.Text);
			await DisplayAlert("Applied", message, "OK");

			// Re-read rather than patching the screen: applying changes what the
			// server will now allow, and it is the server's answer that decides.
			await LoadAsync();
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;

			await DisplayAlert("Could not apply", ex.Message, "OK");

			// A refusal usually means the listing moved on — filled, closed,
			// already applied — so show its current state.
			await LoadAsync();
		}
		finally
		{
			SetBusy(false);
		}
	}

	/// <summary>
	/// Hands an external listing off to the employer's own channel. Opening a
	/// browser or mail client is the only thing UniConnect can do here — these
	/// postings never accept in-app applications.
	/// </summary>
	private async void OnExternalApplyClicked(object? sender, EventArgs e)
	{
		if (_detail is null) return;

		try
		{
			if (_detail.HasExternalUrl)
			{
				await Launcher.Default.OpenAsync(_detail.ExternalApplyUrl!);
			}
			else if (_detail.HasExternalEmail)
			{
				await Launcher.Default.OpenAsync(
					$"mailto:{_detail.ExternalApplyEmail}?subject={Uri.EscapeDataString(_detail.Title)}");
			}
		}
		catch (Exception)
		{
			// A malformed link from the employer, or no app to handle it.
			await DisplayAlert(
				"Could not open",
				_detail.HasExternalUrl
					? $"Open this link yourself: {_detail.ExternalApplyUrl}"
					: $"Email the employer at {_detail.ExternalApplyEmail}",
				"OK");
		}
	}

	private async void OnMyApplicationsClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync($"../{nameof(MyApplicationsPage)}");

	private async void OnBackTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");

	// ---- plumbing ----------------------------------------------------------

	private void SetBusy(bool busy)
	{
		Busy.IsRunning = busy;
		Busy.IsVisible = busy;
		ApplyBtn.IsEnabled = !busy;
	}

	private static Style Theme(string key) => (Style)Application.Current!.Resources[key];

	private async Task<bool> HandleAuthFailureAsync(ApiException ex)
	{
		if (!ex.IsAuthFailure) return false;

		await _session.ClearAsync();
		await Shell.Current.GoToAsync("//login");
		return true;
	}
}
