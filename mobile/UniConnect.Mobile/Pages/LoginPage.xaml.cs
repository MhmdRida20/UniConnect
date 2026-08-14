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

	}

	/// <summary>
	/// Both of these are proportions of the viewport, which XAML has no way to
	/// express, so they are applied here whenever the page is measured.
	/// </summary>
	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);
		if (width <= 0 || height <= 0) return;

		// The gradient covers the top half and the card floats across its lower
		// edge. Clamped so it neither swallows a short landscape window nor
		// leaves a thin stripe on a tall one.
		Band.HeightRequest = Math.Clamp(height * 0.52, 300, 560);

		// Fill the width on a phone, but stop well short of it on a desktop
		// window — a sign-in form stretched across 1400px reads as broken.
		Column.WidthRequest = Math.Min(width - (SideGutter * 2), Responsive.LoginMaxWidth);
	}

	/// <summary>Space left either side of the card at phone widths.</summary>
	private const double SideGutter = 20;

	// ---- hero motion ----

	private CancellationTokenSource? _drift;

	protected override void OnAppearing()
	{
		base.OnAppearing();
		PlayIntro();
		StartAmbientDrift();
	}

	/// <summary>
	/// Motion must stop when the page goes away. Without this the drift loop
	/// keeps animating an off-screen page for the life of the process, which
	/// costs a frame callback forever and keeps the page alive through the
	/// animation's reference to it.
	/// </summary>
	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_drift?.Cancel();
		_drift = null;
	}

	/// <summary>The wordmark rises into place as the screen opens.</summary>
	private async void PlayIntro()
	{
		Wordmark.Opacity = 0;
		Wordmark.TranslationY = 18;

		await Task.WhenAll(
			Wordmark.FadeTo(1, 450, Easing.CubicOut),
			Wordmark.TranslateTo(0, 0, 450, Easing.CubicOut));
	}

	/// <summary>
	/// Drifts the background discs on long, offset cycles so the band never
	/// settles into a still image. Slow on purpose: this is ambient depth, not
	/// something to look at, and short loops on a sign-in screen read as fidget.
	/// </summary>
	private async void StartAmbientDrift()
	{
		_drift?.Cancel();
		var cts = new CancellationTokenSource();
		_drift = cts;

		await Task.WhenAll(
			Drift(Disc1, 26, 18, 7000, cts.Token),
			Drift(Disc2, -22, 24, 9000, cts.Token),
			Drift(Disc3, 20, -16, 8000, cts.Token),
			Drift(Ring1, -18, -20, 10000, cts.Token),
			Drift(Ring2, 14, 22, 6500, cts.Token));
	}

	private static async Task Drift(View view, double dx, double dy, uint duration, CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			await view.TranslateTo(dx, dy, duration, Easing.SinInOut);
			if (token.IsCancellationRequested) break;

			await view.TranslateTo(0, 0, duration, Easing.SinInOut);
		}
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
				await Shell.Current.GoToAsync("//home");
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

			await Shell.Current.GoToAsync("//home");
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

	/// <summary>
	/// Reveals or masks the password.
	/// </summary>
	private void OnTogglePasswordClicked(object? sender, EventArgs e)
	{
		var reveal = PasswordEntry.IsPassword;
		PasswordEntry.IsPassword = !reveal;

		// The icon shows the current state: struck-through eye while the
		// password is masked, open eye once it is visible.
		PasswordToggle.Source = reveal ? "ic_eye_muted.png" : "ic_eye_off_muted.png";
		SemanticProperties.SetDescription(PasswordToggle, reveal ? "Hide password" : "Show password");
	}

	/// <summary>
	/// There is no self-service password reset anywhere in UniConnect — not in
	/// the app and not on the web — so this says who can actually help rather
	/// than pretending to start a flow that does not exist.
	/// </summary>
	private async void OnForgotPasswordTapped(object? sender, TappedEventArgs e)
	{
		await DisplayAlert("Forgot password", "Contact your administrator.", "OK");
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
