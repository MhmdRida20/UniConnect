using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>Typed client over /api/rides. Same response handling as the other clients.</summary>
public class RidesApi
{
    private readonly HttpClient _http;

    public RidesApi(HttpClient http) => _http = http;

    public async Task<List<RideListItemDto>> GetAvailableAsync(CancellationToken ct = default) =>
        await GetAsync<List<RideListItemDto>>("api/rides", ct) ?? new List<RideListItemDto>();

    public async Task<RideDetailsDto> GetDetailsAsync(int id, CancellationToken ct = default) =>
        await GetAsync<RideDetailsDto>($"api/rides/{id}", ct) ?? throw Empty("ride");

    public async Task<MyRidesResponse> GetMineAsync(CancellationToken ct = default) =>
        await GetAsync<MyRidesResponse>("api/rides/mine", ct) ?? new MyRidesResponse();

    public async Task<int> CreateAsync(CreateRideRequest request, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.PostAsJsonAsync("api/rides", request, ApiJson.Options, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("rideId").GetInt32();
    }

    public async Task RequestRideAsync(int rideId, string pickupLocation, CancellationToken ct = default)
    {
        var request = new RequestRideRequest { PickupLocation = pickupLocation };
        var (status, body) = await SendAsync(() => _http.PostAsJsonAsync($"api/rides/{rideId}/request", request, ApiJson.Options, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);
    }

    public async Task CancelRequestAsync(int requestId, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.PostAsync($"api/rides/requests/{requestId}/cancel", null, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);
    }

    public async Task AcceptRequestAsync(int requestId, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.PostAsync($"api/rides/requests/{requestId}/accept", null, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);
    }

    public async Task RejectRequestAsync(int requestId, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.PostAsync($"api/rides/requests/{requestId}/reject", null, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);
    }

    public async Task CancelRideAsync(int rideId, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(() => _http.PostAsync($"api/rides/{rideId}/cancel", null, ct), ct);
        if (!IsSuccess(status)) throw ToException(status, body);
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

    private static ApiException Empty(string what) =>
        new(HttpStatusCode.InternalServerError, $"The server did not return a {what}.", "EMPTY_RESPONSE");
}
