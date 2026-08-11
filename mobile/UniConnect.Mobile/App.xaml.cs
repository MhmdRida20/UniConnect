namespace UniConnect.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// The web app is light-only — its palette is a white surface on a faint
		// green-grey page. Following the device's dark mode would produce a
		// half-ported look that matches neither, so the theme is pinned.
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}