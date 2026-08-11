using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Shared serialiser settings. ASP.NET Core writes camelCase by default while
/// the models here use PascalCase, so case-insensitive matching is what lets
/// the DTOs stay attribute-free.
/// </summary>
internal static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
