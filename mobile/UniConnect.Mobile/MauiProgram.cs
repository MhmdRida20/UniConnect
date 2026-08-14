using Microsoft.Extensions.Logging;
using UniConnect.Mobile.Services;
using ZXing.Net.Maui.Controls;

namespace UniConnect.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeReader() // ZXing.Net.MAUI — needed for ScanPage's CameraBarcodeReaderView
            .ConfigureFonts(fonts =>
            {
                // Body text and labels.
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");

                // Still referenced by a few pages not yet moved to Inter.
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

                // Headings. These are static instances cut from Google's
                // Montserrat variable font — MAUI renders a variable font at its
                // default weight only, so a single file would give every heading
                // the same regular weight.
                fonts.AddFont("Montserrat-SemiBold.ttf", "MontserratSemiBold");
                fonts.AddFont("Montserrat-Bold.ttf", "MontserratBold");
            });

        StripNativeInputBorders();

        // Holds the bearer token; the HttpClient's handler chain reads from it
        // on every request, so it has to be a singleton alongside the client.
        builder.Services.AddSingleton<SessionStore>();

        // One long-lived client for the whole app. Creating HttpClient per call
        // leaks sockets, and the base address never changes while the app runs.
        builder.Services.AddSingleton(sp => ApiHttp.CreateClient(sp.GetRequiredService<SessionStore>()));

		builder.Services.AddSingleton<AuthApi>();
		builder.Services.AddSingleton<StudyGroupsApi>();
		builder.Services.AddSingleton<InternshipsApi>();
		builder.Services.AddSingleton<ProfileApi>();
		builder.Services.AddSingleton<HomeApi>();

		// One cached profile for the whole app, so every app bar draws the same
		// avatar and a new picture reaches all of them at once.
		builder.Services.AddSingleton<ProfileStore>();
		builder.Services.AddSingleton<NotificationsApi>();
        builder.Services.AddSingleton<AuthApi>();
        builder.Services.AddSingleton<StudyGroupsApi>();
        builder.Services.AddSingleton<InternshipsApi>();
        builder.Services.AddSingleton<NotificationsApi>();
        builder.Services.AddSingleton<AttendanceApi>();

        // One hub connection shared by every screen that wants live updates.
        builder.Services.AddSingleton<StudyGroupHubClient>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Every text input in this app sits inside a Border (UcInputFrame) that
    /// draws the field's outline. The native controls draw their own on top of
    /// it — WinUI a focus underline, Android the Material underline — which
    /// reads as a line through the middle of the field rather than a border.
    /// Removing the native chrome leaves the Border as the only outline.
    /// </summary>
    private static void StripNativeInputBorders()
    {
#if WINDOWS
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("UcNoBorder",
            (handler, view) => StripWinUiChrome(handler.PlatformView));

        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("UcNoBorder",
            (handler, view) => StripWinUiChrome(handler.PlatformView));

        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("UcNoBorder",
            (handler, view) => StripWinUiChrome(handler.PlatformView));
#elif ANDROID
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("UcNoBorder", (handler, view) =>
			handler.PlatformView.Background = null);

		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("UcNoBorder", (handler, view) =>
			handler.PlatformView.Background = null);

		Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("UcNoBorder", (handler, view) =>
			handler.PlatformView.Background = null);
#endif
    }

#if WINDOWS
    /// <summary>
    /// Clears a WinUI input's own borders and fills.
    ///
    /// Setting BorderThickness alone is not enough: the resting and focused
    /// borders come from theme resources baked into the control template
    /// (TextControlBorderThemeThicknessFocused is 0,0,0,2, which is the accent
    /// underline that appears the moment a field is focused). Overriding the
    /// resources on the element itself is what actually removes them, since a
    /// local Resources entry wins over the application-level one.
    /// </summary>
    private static void StripWinUiChrome(Microsoft.UI.Xaml.FrameworkElement view)
    {
        var none = new Microsoft.UI.Xaml.Thickness(0);
        var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

        view.Resources["TextControlBorderThemeThickness"] = none;
        view.Resources["TextControlBorderThemeThicknessFocused"] = none;
        view.Resources["TextControlBackground"] = clear;
        view.Resources["TextControlBackgroundPointerOver"] = clear;
        view.Resources["TextControlBackgroundFocused"] = clear;
        view.Resources["ComboBoxBorderThemeThickness"] = none;
        view.Resources["ComboBoxBackground"] = clear;
        view.Resources["ComboBoxBackgroundPointerOver"] = clear;
        view.Resources["ComboBoxBackgroundFocused"] = clear;

        if (view is Microsoft.UI.Xaml.Controls.Control control)
        {
            control.BorderThickness = none;
            control.Background = clear;
        }
    }
#endif
}
