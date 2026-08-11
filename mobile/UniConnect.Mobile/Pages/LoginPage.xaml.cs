using UniConnect.Mobile.Services;

namespace UniConnect.Mobile.Pages;

public partial class LoginPage : ContentPage
{
	private readonly AuthApi _auth;
	private readonly SessionStore _session;
	private bool _resumeChecked;

	public LoginPage()
	{
		InitializeComponent();

		_auth = ServiceHelper.Get<AuthApi>();
		_session = ServiceHelper.Get<SessionStore>();

		// Shown small at the bottom so a wrong address is obvious during
		// development instead of surfacing as a mysterious timeout.
		TargetLabel.Text = ApiConfig.BaseAddress.ToString();

		// A sign-in form stretched across a desktop window reads as broken, so
		// the column stops growing well before the window does.
		FormColumn.MaximumWidthRequest = Responsive.LoginMaxWidth;
	}

	/// <summary>
	/// A token that survived the last run means the student is already signed
	/// in; skip straight past this screen. Signing out clears it, so this
	/// cannot trap the user in a loop.
	///
	/// Deliberately not OnAppearing: at launch that runs while Shell is still
	/// building its first section, and navigating from there kills the process
	/// with "Pending Navigations still processing". OnNavigatedTo fires once
	/// the initial navigation has finished, and the dispatch puts the redirect
	/// on the next turn of the UI loop so it cannot overlap.
	/// </summary>
	protected override void OnNavigatedTo(NavigatedToEventArgs args)
	{
		base.OnNavigatedTo(args);

		if (_resumeChecked) return;
		_resumeChecked = true;

		Dispatcher.Dispatch(async () =>
		{
			if (await _session.HasValidTokenAsync())
				await Shell.Current.GoToAsync("//groups");
		});
	}

	private async void OnSignInClicked(object? sender, EventArgs e)
	{
		var email = EmailEntry.Text?.Trim() ?? string.Empty;
		var password = PasswordEntry.Text ?? string.Empty;

		if (email.Length == 0 || password.Length == 0)
		{
			ShowError("Enter your email and password.");
			return;
		}

		SetBusy(true);
		try
		{
			await _auth.LoginAsync(email, password);

			// Nothing sensitive should outlive a successful sign-in.
			PasswordEntry.Text = string.Empty;

			await Shell.Current.GoToAsync("//groups");
		}
		catch (ApiException ex)
		{
			// The server writes these sentences for students already — 423
			// lockout, suspended account, wrong role — so they are shown as-is.
			ShowError(ex.Message);
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
		SignInBtn.IsEnabled = !busy;
		EmailEntry.IsEnabled = !busy;
		PasswordEntry.IsEnabled = !busy;

		if (busy) ErrorBox.IsVisible = false;
	}

	private void ShowError(string message)
	{
		ErrorLabel.Text = message;
		ErrorBox.IsVisible = true;
	}
}
