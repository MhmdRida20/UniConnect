using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class ProfilePage : ContentPage
{
	private readonly ProfileApi _api;
	private readonly ProfileStore _store;
	private readonly SessionStore _session;
	private readonly StudyGroupHubClient _hub;

	private ProfileDto? _profile;

	/// <summary>
	/// The picture the student has chosen but not yet saved. Held rather than
	/// uploaded on pick, so "Save changes" is what commits both fields and
	/// cancelling out of the screen changes nothing.
	/// </summary>
	private FileResult? _pendingPicture;

	public ProfilePage()
	{
		InitializeComponent();

		_api = ServiceHelper.Get<ProfileApi>();
		_store = ServiceHelper.Get<ProfileStore>();
		_session = ServiceHelper.Get<SessionStore>();
		_hub = ServiceHelper.Get<StudyGroupHubClient>();

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
			_profile = await _api.GetAsync();
			_store.Set(_profile);
			Render(_profile);
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

	private void Render(ProfileDto p)
	{
		NameLabel.Text = p.FullName;
		EmailLabel.Text = p.Email;
		UniversityIdLabel.Text = p.UniversityId;
		UniversityLabel.Text = p.UniversityCode;
		AvatarInitials.Text = p.Initials;

		// Only overwrite the field when it is not being edited, so a reload
		// triggered by pull-to-refresh cannot wipe what is being typed.
		if (!PhoneEntry.IsFocused) PhoneEntry.Text = p.PhoneNumber;

		ShowPicture(p.HasPicture ? p.ProfilePictureUrl : null);
	}

	/// <summary>
	/// Swaps between the photo and the monogram underneath it. A null URL means
	/// there is no picture, so the initials show through.
	/// </summary>
	private void ShowPicture(string? url)
	{
		AvatarImage.Source = url is null ? null : ImageSource.FromUri(new Uri(url));
		AvatarImage.IsVisible = url is not null;
		RemovePictureBtn.IsVisible = url is not null;
	}

	// ---- editing -----------------------------------------------------------

	private async void OnChoosePictureClicked(object? sender, EventArgs e)
	{
		try
		{
			var file = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
			{
				Title = "Choose a profile picture"
			});

			if (file is null) return;

			_pendingPicture = file;

			// Previewed straight from the local file, so the choice is visible
			// before it is uploaded.
			AvatarImage.Source = ImageSource.FromFile(file.FullPath);
			AvatarImage.IsVisible = true;
			ChoosePictureBtn.Text = "Photo selected — save to apply";
		}
		catch (FeatureNotSupportedException)
		{
			await DisplayAlert("Not supported", "Picking a photo is not supported on this device.", "OK");
		}
		catch (PermissionException)
		{
			await DisplayAlert("Permission needed", "UniConnect needs permission to open your photos.", "OK");
		}
	}

	private async void OnRemovePictureClicked(object? sender, EventArgs e)
	{
		// A pending pick has not been uploaded, so discarding it is local only.
		if (_pendingPicture is not null)
		{
			_pendingPicture = null;
			ChoosePictureBtn.Text = "Choose a photo";
			ShowPicture(_profile?.ProfilePictureUrl);
			return;
		}

		var confirmed = await DisplayAlert(
			"Remove picture", "Remove your profile picture?", "Remove", "Cancel");
		if (!confirmed) return;

		await RunAsync(async () =>
		{
			var message = await _api.RemovePictureAsync();
			await ReloadAndNotifyAsync();
			return message;
		});
	}

	private async void OnSaveClicked(object? sender, EventArgs e) =>
		await RunAsync(async () =>
		{
			// Phone first: if the upload is refused, the number is already
			// saved and the message explains what was not.
			var message = await _api.UpdatePhoneAsync(PhoneEntry.Text);

			if (_pendingPicture is not null)
			{
				await using var stream = await _pendingPicture.OpenReadAsync();
				await _api.UploadPictureAsync(stream, _pendingPicture.FileName);

				_pendingPicture = null;
				ChoosePictureBtn.Text = "Choose a photo";
			}

			await ReloadAndNotifyAsync();
			return message;
		});

	/// <summary>
	/// Re-reads the profile and pushes it into the shared store, which is what
	/// makes every other screen's app bar avatar update immediately.
	/// </summary>
	private async Task ReloadAndNotifyAsync()
	{
		_profile = await _api.GetAsync();
		_store.Set(_profile);
		Render(_profile);
	}

	// ---- session -----------------------------------------------------------

	private async void OnSignOutClicked(object? sender, EventArgs e)
	{
		var confirmed = await DisplayAlert("Sign out", "Sign out of UniConnect on this device?", "Sign out", "Cancel");
		if (!confirmed) return;

		// Drop the hub connection too — it is authenticated as the outgoing
		// student and would otherwise keep receiving their groups' traffic.
		await _hub.StopAsync();
		await _session.ClearAsync();
		_store.Clear();

		await Shell.Current.GoToAsync("//login");
	}

	// ---- plumbing ----------------------------------------------------------

	private async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

	private async void OnBackTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");

	/// <summary>Runs a call with the busy state on, and reports either outcome.</summary>
	private async Task RunAsync(Func<Task<string>> action)
	{
		SetBusy(true);
		try
		{
			var message = await action();
			await DisplayAlert("Profile", message, "OK");
		}
		catch (ApiException ex)
		{
			if (await HandleAuthFailureAsync(ex)) return;
			await DisplayAlert("Could not save", ex.Message, "OK");
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void SetBusy(bool busy)
	{
		Busy.IsRunning = busy;
		Busy.IsVisible = busy;
		SaveBtn.IsEnabled = !busy;
		ChoosePictureBtn.IsEnabled = !busy;
	}

	private async Task<bool> HandleAuthFailureAsync(ApiException ex)
	{
		if (!ex.IsAuthFailure) return false;

		await _session.ClearAsync();
		_store.Clear();
		await Shell.Current.GoToAsync("//login");
		return true;
	}
}
