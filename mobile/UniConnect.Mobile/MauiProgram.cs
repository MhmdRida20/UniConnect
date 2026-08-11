using Microsoft.Extensions.Logging;
using UniConnect.Mobile.Services;

namespace UniConnect.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Holds the bearer token; the HttpClient's handler chain reads from it
		// on every request, so it has to be a singleton alongside the client.
		builder.Services.AddSingleton<SessionStore>();

		// One long-lived client for the whole app. Creating HttpClient per call
		// leaks sockets, and the base address never changes while the app runs.
		builder.Services.AddSingleton(sp => ApiHttp.CreateClient(sp.GetRequiredService<SessionStore>()));

		builder.Services.AddSingleton<AuthApi>();
		builder.Services.AddSingleton<StudyGroupsApi>();

		// One hub connection shared by every screen that wants live updates.
		builder.Services.AddSingleton<StudyGroupHubClient>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
