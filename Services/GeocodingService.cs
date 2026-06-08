using System.Text.Json;

namespace UniConnect.Services
{
    /// <summary>
    /// Converts a text address (e.g. "Hamra, Beirut") into map coordinates
    /// (latitude/longitude) using OpenStreetMap's free Nominatim service.
    ///
    /// No API key or signup required. Nominatim asks that we:
    ///   - send a descriptive User-Agent (done below)
    ///   - keep requests modest (fine for a student project)
    ///
    /// If geocoding fails for any reason, we return null and the caller simply
    /// stores no coordinates — the ride still works, it just won't have a map pin.
    /// </summary>
    public interface IGeocodingService
    {
        Task<(double lat, double lng)?> GeocodeAsync(string address);
    }

    public class NominatimGeocodingService : IGeocodingService
    {
        private readonly HttpClient _http;
        private readonly ILogger<NominatimGeocodingService> _logger;

        public NominatimGeocodingService(HttpClient http, ILogger<NominatimGeocodingService> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<(double lat, double lng)?> GeocodeAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            try
            {
                // Nominatim search endpoint, asking for JSON, limited to the best 1 result.
                var url = $"https://nominatim.openstreetmap.org/search" +
                          $"?q={Uri.EscapeDataString(address)}&format=json&limit=1";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                // Nominatim REQUIRES a User-Agent identifying your app.
                request.Headers.UserAgent.ParseAdd("UniConnect-StudentProject/1.0");

                var resp = await _http.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Geocoding failed for '{Address}': {Status}", address, resp.StatusCode);
                    return null;
                }

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.GetArrayLength() == 0)
                    return null;

                var first = doc.RootElement[0];
                var latStr = first.GetProperty("lat").GetString();
                var lonStr = first.GetProperty("lon").GetString();

                if (double.TryParse(latStr, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(lonStr, System.Globalization.CultureInfo.InvariantCulture, out var lng))
                {
                    return (lat, lng);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Geocoding threw for '{Address}'", address);
                return null;  // fail gracefully — never block ride creation
            }
        }
    }
}
