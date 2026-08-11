using Microsoft.Extensions.DependencyInjection;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Reaches the app's service provider from a page constructor.
///
/// Shell builds pages two ways — from a DataTemplate and from a registered
/// route — and neither reliably runs them through constructor injection. Pages
/// therefore keep parameterless constructors and pull what they need through
/// here. It is a service locator, which is not the pattern to reach for
/// generally, but it keeps page construction uniform under Shell.
/// </summary>
internal static class ServiceHelper
{
    public static T Get<T>() where T : notnull =>
        IPlatformApplication.Current!.Services.GetRequiredService<T>();
}
