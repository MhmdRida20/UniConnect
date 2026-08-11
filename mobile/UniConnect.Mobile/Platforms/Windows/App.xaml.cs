using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace UniConnect.Mobile.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		// A WinUI app has no console, so an unhandled exception ends the
		// process with a bare 0xC0000409 and nothing to read. Writing it to a
		// file makes startup failures — a bad StaticResource key, a missing DI
		// registration — diagnosable without attaching a debugger.
		AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
		UnhandledException += (_, e) => LogCrash(e.Exception);

		this.InitializeComponent();
	}

	private static void LogCrash(Exception? ex)
	{
		if (ex is null) return;

		try
		{
			var path = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"UniConnect.Mobile.crash.log");

			File.AppendAllText(path, $"=== {DateTime.Now:O} ==={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
		}
		catch (Exception)
		{
			// Nothing useful to do if even the log write fails.
		}
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

