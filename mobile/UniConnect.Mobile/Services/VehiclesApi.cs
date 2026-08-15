using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

public class VehiclesApi
{
    private readonly HttpClient _http;

    public VehiclesApi(HttpClient http) => _http = http;

    public async Task<List<VehicleDto>> GetMineAsync(CancellationToken ct = default) =>
        await GetAsync<List<VehicleDto>>("api/vehicles", ct) ?? new List<VehicleDto>();

    public async Task<int> CreateAsync(CreateVehicleRequest request, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.PostAsJsonAsync("api/vehicles", request, ApiJson.Options, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("vehicleId").GetInt32();
    }

    // ---- plumbing (identical to the sibling API clients) -----------------------

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var (status, body) = await SendAsync(() => _http.GetAsync(url, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);
        return Parse<T>(body);
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            using var response = await send();
            return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(
                HttpStatusCode.ServiceUnavailable,
                $"Could not reach the server at {ApiConfig.BaseAddress}. {ex.GetBaseException().Message}",
                "NO_CONNECTION");
        }
        catch (TaskCanceledException)
        {
            throw new ApiException(HttpStatusCode.RequestTimeout, "The server took too long to respond.", "TIMEOUT");
        }
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static T? Parse<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return default;
        try { return JsonSerializer.Deserialize<T>(body, ApiJson.Options); }
        catch (JsonException) { return default; }
    }

    private static ApiException ToException(HttpStatusCode status, string body)
    {
        var error = Parse<ApiError>(body);
        var message = error?.Error ?? status switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            HttpStatusCode.Forbidden => "This account cannot use the mobile app.",
            _ => $"The server returned {(int)status}."
        };
        return new ApiException(status, message, error?.Code);
    }
}
