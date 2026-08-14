using System.Net;
using System.Text.Json;

namespace UniConnect.Mobile.Services;

/// <summary>
/// The service codes the server uses, mirrored here.
///
/// Hand-copied from Models/Service.cs for the same reason the DTOs are: a
/// project reference would drag the whole server stack into the app. These are
/// the platform's stable identifiers — they are what the dashboard keys its
/// icons and its navigation off, since the row's IconClass is a Bootstrap class
/// name that means nothing to MAUI.
/// </summary>
public static class ServiceCodes
{
	public const string StudyGroups = "StudyGroups";
	public const string RideSharing = "RideSharing";
	public const string Attendance = "Attendance";
	public const string Tickets = "Tickets";
	public const string Internships = "Internships";
	public const string Clubs = "Clubs";
}

/// <summary>The four counters shown on the dashboard.</summary>
public class DashboardStats
{
	public int GroupsJoined { get; set; }
	public int RidesTaken { get; set; }
	public int CoursesEnrolled { get; set; }
	public int ClubsJoined { get; set; }
}

/// <summary>
/// One service the student's university has switched on.
///
/// IconClass comes back as a Bootstrap class name ("bi-people"), which means
/// nothing to MAUI — the app maps <see cref="Code"/> to an image instead, so
/// the field is carried but not used for display.
/// </summary>
public class ServiceSummary
{
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string? IconClass { get; set; }
}

/// <summary>What GET /api/home/dashboard returns.</summary>
public class DashboardDto
{
	public string FullName { get; set; } = string.Empty;
	public string UniversityCode { get; set; } = string.Empty;
	public string UniversityName { get; set; } = string.Empty;
	public DashboardStats Stats { get; set; } = new();
	public List<ServiceSummary> EnabledServices { get; set; } = new();

	/// <summary>Just the given name — "Good morning, Ali" beats the full name.</summary>
	public string FirstName =>
		FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? FullName;

	public bool HasService(string code) =>
		EnabledServices.Any(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Typed client over /api/home. Same response handling as the other clients:
/// the body is read once to a string and parsed from there.
/// </summary>
public class HomeApi
{
	private readonly HttpClient _http;

	public HomeApi(HttpClient http) => _http = http;

	public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
	{
		var (status, body) = await SendAsync(() => _http.GetAsync("api/home/dashboard", ct), ct);
		if (!IsSuccess(status)) throw ToException(status, body);

		return Parse<DashboardDto>(body)
			?? throw new ApiException(status, "The server returned an empty dashboard.", "EMPTY_RESPONSE");
	}

	// ---- plumbing ---------------------------------------------------------

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

		try
		{
			return JsonSerializer.Deserialize<T>(body, ApiJson.Options);
		}
		catch (JsonException)
		{
			return default;
		}
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
