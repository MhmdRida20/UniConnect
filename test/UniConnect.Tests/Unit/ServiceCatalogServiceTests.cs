using UniConnect.Models;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Unit;

/// <summary>
/// Per-university service enablement — the data behind both the nav bar and
/// RequireServiceAttribute's server-side block.
///
/// The subtlety worth pinning: a service can be switched on for a university
/// and still not be available, because the platform hasn't implemented it yet.
/// Both flags have to agree.
/// </summary>
public class ServiceCatalogServiceTests : IDisposable
{
    private readonly TestDatabase _test = new();

    public ServiceCatalogServiceTests() => _test.Db.AddUniversity();

    public void Dispose() => _test.Dispose();

    private ServiceCatalogService Service() => new(_test.Db);

    private void Register(string code, bool implemented, bool enabled,
        string universityCode = TestData.DefaultUniversity)
    {
        if (!_test.Db.Services.Any(s => s.Code == code))
            _test.Db.Services.Add(new Service { Code = code, Name = code, IsImplemented = implemented });

        _test.Db.UniversityServices.Add(new UniversityService
        {
            UniversityCode = universityCode,
            ServiceCode = code,
            IsEnabled = enabled
        });
        _test.Db.SaveChanges();
    }

    [Fact]
    public async Task Enabled_and_implemented_is_available()
    {
        Register(ServiceCodes.Clubs, implemented: true, enabled: true);

        Assert.True(await Service().IsServiceEnabledAsync(TestData.DefaultUniversity, ServiceCodes.Clubs));
    }

    [Fact]
    public async Task Enabled_but_not_yet_implemented_is_not_available()
    {
        // An admin can toggle a service on before the platform supports it;
        // that must not open the route.
        Register(ServiceCodes.Clubs, implemented: false, enabled: true);

        Assert.False(await Service().IsServiceEnabledAsync(TestData.DefaultUniversity, ServiceCodes.Clubs));
    }

    [Fact]
    public async Task Implemented_but_switched_off_is_not_available()
    {
        Register(ServiceCodes.Clubs, implemented: true, enabled: false);

        Assert.False(await Service().IsServiceEnabledAsync(TestData.DefaultUniversity, ServiceCodes.Clubs));
    }

    [Fact]
    public async Task A_service_with_no_row_at_all_is_not_available()
    {
        Assert.False(await Service().IsServiceEnabledAsync(TestData.DefaultUniversity, ServiceCodes.Clubs));
    }

    [Fact]
    public async Task One_universitys_enablement_does_not_leak_into_another()
    {
        _test.Db.AddUniversity(TestData.OtherUniversity);
        Register(ServiceCodes.Clubs, implemented: true, enabled: true, universityCode: TestData.OtherUniversity);

        Assert.True(await Service().IsServiceEnabledAsync(TestData.OtherUniversity, ServiceCodes.Clubs));
        Assert.False(await Service().IsServiceEnabledAsync(TestData.DefaultUniversity, ServiceCodes.Clubs));
    }

    [Fact]
    public async Task Enabled_codes_list_excludes_unimplemented_and_disabled_services()
    {
        Register(ServiceCodes.Clubs, implemented: true, enabled: true);
        Register(ServiceCodes.Tickets, implemented: true, enabled: true);
        Register(ServiceCodes.RideSharing, implemented: true, enabled: false);
        Register(ServiceCodes.Internships, implemented: false, enabled: true);

        var codes = await Service().GetEnabledServiceCodesAsync(TestData.DefaultUniversity);

        Assert.Equal(
            new[] { ServiceCodes.Clubs, ServiceCodes.Tickets }.OrderBy(c => c),
            codes.OrderBy(c => c));
    }
}
