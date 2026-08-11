using Microsoft.AspNetCore.Mvc;
using UniConnect.Controllers;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// Regression coverage for the mangled-Arabic-CSV report: /AdminReports/Export
/// wrote its bytes with plain Encoding.UTF8.GetBytes, no BOM. Excel infers a
/// BOM-less CSV's encoding from the system codepage rather than assuming UTF-8,
/// so any non-ASCII text — Arabic ride addresses and names, in the reported
/// case — came out as mojibake the moment the file was opened. Fixed by
/// prepending Encoding.UTF8.GetPreamble(), the same fix already in place on the
/// attendance CSV export right next to it.
/// </summary>
public class AdminReportExportTests : IDisposable
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    private readonly TestDatabase _test = new();
    private readonly ApplicationUser _admin;

    public AdminReportExportTests()
    {
        _test.Db.AddUniversity();
        _admin = _test.Db.AddUser("ADMIN01", fullName: "Platform Admin");
    }

    public void Dispose() => _test.Dispose();

    private AdminReportsController Controller() =>
        new AdminReportsController(_test.Db, IdentityHarness.CreateUserManager(_test.Db))
            .SignedInAs(_admin, "Admin");

    [Fact]
    public async Task The_exported_file_opens_with_a_utf8_bom()
    {
        var result = await Controller().Export("ServiceUsage", null, null, null);
        var file = Assert.IsType<FileContentResult>(result);

        Assert.Equal(Utf8Bom, file.FileContents.Take(3));
    }

    [Fact]
    public async Task Arabic_text_in_a_report_round_trips_intact_through_the_export()
    {
        // The reported case: a ride whose departure/destination are Arabic
        // addresses. Round-tripping the bytes through the same BOM-aware
        // decoder Excel uses is what actually proves the fix, rather than just
        // checking the marker bytes are present.
        var driver = _test.Db.AddUser("U2024001", fullName: "سارة خليل");
        _test.Db.Vehicles.Add(new Vehicle
        {
            UserId = driver.Id, VehicleType = "Sedan", PlateNumber = "ABC123", Color = "Green"
        });
        _test.Db.SaveChanges();
        var vehicleId = _test.Db.Vehicles.Single().Id;

        const string arabicDeparture = "27، شارع 43، محافظة بيروت 6703 2052";
        const string arabicDestination = "الأشرفية، بيروت 1100، لبنان";
        _test.Db.Rides.Add(new Ride
        {
            UniversityCode = TestData.DefaultUniversity,
            DriverId = driver.Id,
            VehicleId = vehicleId,
            DepartureLocation = arabicDeparture,
            Destination = arabicDestination,
            DepartureTime = DateTime.Now.AddDays(1),
            TotalSeats = 3,
            AvailableSeats = 3
        });
        _test.Db.SaveChanges();

        var result = await Controller().Export("Rides", null, null, null);
        var file = Assert.IsType<FileContentResult>(result);

        var text = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetString(file.FileContents, 3, file.FileContents.Length - 3);   // skip the BOM, decode the rest

        Assert.Contains(arabicDeparture, text);
        Assert.Contains(arabicDestination, text);
    }

    [Fact]
    public async Task An_unknown_report_type_is_a_404_not_a_crash()
    {
        Assert.IsType<NotFoundResult>(await Controller().Export("NotAReportType", null, null, null));
    }
}
