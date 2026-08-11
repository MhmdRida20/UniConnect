using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Unit;

/// <summary>
/// Regression coverage for the crash reported against /AdminUniversities/Create:
/// a malformed ApiBaseUrl threw UriFormatException out of `new Uri(...)`, before
/// the try/catch that handles every other kind of sync failure had even been
/// entered — so instead of the usual "LastSyncStatus = Failed", the whole
/// request 500'd and the University row was left half-provisioned (saved, but
/// with no accounts, no service catalog).
/// </summary>
public class UniversityApiSyncRunnerTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly FakeHttpClientFactory _http = new();
    private readonly FakeUniversityProvider _provider = new();

    public void Dispose() => _test.Dispose();

    // The resolver is only reached on the UMS-style sync path, which these
    // tests never take — but it is a required constructor dependency, so the
    // fake (which implements both the provider and the resolver) stands in.
    private UniversityApiSyncRunner Runner() =>
        new(_test.Db, _http, _provider, NullLogger<UniversityApiSyncRunner>.Instance);

    private University AddUniversity(string apiBaseUrl)
    {
        var university = new University
        {
            Code = "TEST", Name = "Test University", ApiBaseUrl = apiBaseUrl, ApiKey = "key", IsActive = true
        };
        _test.Db.Universities.Add(university);
        _test.Db.SaveChanges();
        return university;
    }

    [Theory]
    [InlineData("not a url at all")]
    [InlineData("registrar.example.edu/api")]     // missing scheme — the reported case
    [InlineData("ftp://registrar.example.edu")]    // a scheme, just not one HttpClient accepts
    [InlineData("   ")]
    public async Task A_malformed_api_base_url_is_recorded_as_a_failed_sync_not_a_crash(string badUrl)
    {
        var university = AddUniversity(badUrl);

        var exception = await Record.ExceptionAsync(() => Runner().SyncOneUniversityAsync(university));

        Assert.Null(exception);
        Assert.Equal("Failed", university.LastSyncStatus);
        Assert.NotNull(university.LastSyncError);
        Assert.NotNull(university.LastSyncAt);
    }

    [Fact]
    public async Task A_malformed_url_never_reaches_the_network()
    {
        var university = AddUniversity("definitely not a uri");

        await Runner().SyncOneUniversityAsync(university);

        Assert.Empty(_http.Handler.Requests);
    }

    [Fact]
    public async Task A_well_formed_url_still_syncs_normally()
    {
        // Guards against the fix over-correcting into rejecting valid input.
        var university = AddUniversity("https://registrar.example.edu/api/v1");
        _http.Handler
            .RespondTo("health", HttpStatusCode.OK)
            .RespondTo("students", HttpStatusCode.OK, "[]")
            .RespondTo("courses", HttpStatusCode.OK, "[]");

        await Runner().SyncOneUniversityAsync(university);

        Assert.NotEmpty(_http.Handler.Requests);
        Assert.NotEqual("Failed", university.LastSyncStatus);
    }

    [Fact]
    public async Task A_second_call_after_a_bad_url_still_reads_updated_config()
    {
        // The admin fixes the address and clicks Sync again — that path must
        // not still be poisoned by the first attempt.
        var university = AddUniversity("garbage");
        await Runner().SyncOneUniversityAsync(university);
        Assert.Equal("Failed", university.LastSyncStatus);

        university.ApiBaseUrl = "https://registrar.example.edu/api/v1";
        _test.Db.SaveChanges();
        _http.Handler
            .RespondTo("health", HttpStatusCode.OK)
            .RespondTo("students", HttpStatusCode.OK, "[]")
            .RespondTo("courses", HttpStatusCode.OK, "[]");

        await Runner().SyncOneUniversityAsync(university);

        Assert.NotEqual("Failed", university.LastSyncStatus);
    }
}
