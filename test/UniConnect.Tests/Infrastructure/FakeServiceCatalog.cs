using UniConnect.Services;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// Per-university service enablement, without going through the database.
///
/// Everything is enabled by default so a test that isn't about service gating
/// doesn't have to say so. Call <see cref="Disable"/> to exercise the
/// RequireServiceAttribute path.
/// </summary>
public sealed class FakeServiceCatalog : IServiceCatalogService
{
    private readonly HashSet<(string University, string Service)> _disabled = new();

    public FakeServiceCatalog Disable(string universityCode, string serviceCode)
    {
        _disabled.Add((universityCode, serviceCode));
        return this;
    }

    public Task<bool> IsServiceEnabledAsync(string universityCode, string serviceCode)
        => Task.FromResult(!_disabled.Contains((universityCode, serviceCode)));

    public Task<List<string>> GetEnabledServiceCodesAsync(string universityCode)
        => Task.FromResult(new[]
            {
                UniConnect.Models.ServiceCodes.StudyGroups,
                UniConnect.Models.ServiceCodes.RideSharing,
                UniConnect.Models.ServiceCodes.Attendance,
                UniConnect.Models.ServiceCodes.Tickets,
                UniConnect.Models.ServiceCodes.Internships,
                UniConnect.Models.ServiceCodes.Clubs
            }
            .Where(code => !_disabled.Contains((universityCode, code)))
            .ToList());
}
