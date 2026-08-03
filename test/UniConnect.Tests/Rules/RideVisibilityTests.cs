using UniConnect.Controllers;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// FR-09 — which rides a student is shown, and who may request a seat.
///
/// The browse list carries six conditions at once (same university, active,
/// seats left, not mine, not departed, in the future). Any one of them going
/// missing looks like a slightly longer list, not like a bug, which is why each
/// gets its own case here.
/// </summary>
public class RideVisibilityTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly StubHubContext<RideTrackingHub> _hub = new();
    private readonly ApplicationUser _driver;
    private readonly ApplicationUser _passenger;
    private readonly Vehicle _vehicle;

    public RideVisibilityTests()
    {
        _test.Db.AddUniversity();
        _driver = _test.Db.AddUser("U2024001", fullName: "Dana Driver");
        _passenger = _test.Db.AddUser("U2024002", fullName: "Pat Passenger");

        _vehicle = new Vehicle
        {
            UserId = _driver.Id, VehicleType = "Sedan",
            PlateNumber = "ABC123", Color = "Green", SeatCapacity = 4
        };
        _test.Db.Vehicles.Add(_vehicle);
        _test.Db.SaveChanges();
    }

    public void Dispose() => _test.Dispose();

    private RidesController Controller(ApplicationUser user) =>
        new RidesController(
                _test.Db,
                IdentityHarness.CreateUserManager(_test.Db),
                new StubGeocoder(),
                _hub,
                ServiceHarness.AuditLog(_test.Db),
                ServiceHarness.Notifications(_test.Db))
            .SignedInAs(user, "Student");

    private Ride AddRide(
        ApplicationUser? driver = null,
        string universityCode = TestData.DefaultUniversity,
        int availableSeats = 3,
        RideStatus status = RideStatus.Active,
        double departsInHours = 2,
        DateTime? tripStartedAt = null)
    {
        var ride = new Ride
        {
            UniversityCode = universityCode,
            DriverId = (driver ?? _driver).Id,
            VehicleId = _vehicle.Id,
            DepartureLocation = "Hamra",
            Destination = "Main Gate",
            DepartureTime = DateTime.Now.AddHours(departsInHours),
            TotalSeats = 3,
            AvailableSeats = availableSeats,
            Status = status,
            TripStartedAt = tripStartedAt
        };
        _test.Db.Rides.Add(ride);
        _test.Db.SaveChanges();
        return ride;
    }

    private async Task<List<Ride>> VisibleTo(ApplicationUser user) =>
        (await Controller(user).Index()).ShouldBeViewWithModel<List<Ride>>();

    // ---------- What appears in the list ----------

    [Fact]
    public async Task An_ordinary_upcoming_ride_is_visible_to_other_students()
    {
        var ride = AddRide();

        Assert.Equal(new[] { ride.Id }, (await VisibleTo(_passenger)).Select(r => r.Id));
    }

    [Fact]
    public async Task Your_own_ride_is_not_in_your_browse_list()
    {
        // Drivers manage theirs from My Rides; showing it here would just
        // invite them to request their own seat.
        AddRide();

        Assert.Empty(await VisibleTo(_driver));
    }

    [Fact]
    public async Task A_ride_with_no_seats_left_is_hidden()
    {
        AddRide(availableSeats: 0);

        Assert.Empty(await VisibleTo(_passenger));
    }

    [Theory]
    [InlineData(RideStatus.Full)]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public async Task A_ride_that_is_not_active_is_hidden(RideStatus status)
    {
        AddRide(status: status);

        Assert.Empty(await VisibleTo(_passenger));
    }

    [Fact]
    public async Task A_ride_whose_departure_time_has_passed_is_hidden()
    {
        AddRide(departsInHours: -1);

        Assert.Empty(await VisibleTo(_passenger));
    }

    [Fact]
    public async Task A_ride_that_has_already_set_off_is_hidden()
    {
        // Departure time still in the future, but the driver hit Start Trip.
        AddRide(tripStartedAt: DateTime.Now.AddMinutes(-5));

        Assert.Empty(await VisibleTo(_passenger));
    }

    [Fact]
    public async Task Rides_from_another_university_are_hidden()
    {
        _test.Db.AddUniversity(TestData.OtherUniversity);
        var foreignDriver = _test.Db.AddUser("U2024003", TestData.OtherUniversity);
        AddRide(driver: foreignDriver, universityCode: TestData.OtherUniversity);

        Assert.Empty(await VisibleTo(_passenger));
    }

    [Fact]
    public async Task Rides_are_ordered_by_departure_time()
    {
        var later = AddRide(departsInHours: 5);
        var sooner = AddRide(departsInHours: 1);

        Assert.Equal(new[] { sooner.Id, later.Id }, (await VisibleTo(_passenger)).Select(r => r.Id));
    }

    // ---------- Requesting a seat ----------

    [Fact]
    public async Task A_student_cannot_request_their_own_ride()
    {
        var ride = AddRide();
        var controller = Controller(_driver);

        await controller.RequestRide(ride.Id, "Gate A", null, null);

        Assert.Equal("You cannot request your own ride.", controller.TempData["Error"]);
        Assert.Empty(_test.NewContext().RideRequests);
    }

    [Fact]
    public async Task A_request_needs_a_pickup_location()
    {
        var ride = AddRide();
        var controller = Controller(_passenger);

        await controller.RequestRide(ride.Id, "   ", null, null);

        Assert.Equal("Please enter a valid pickup location.", controller.TempData["Error"]);
        Assert.Empty(_test.NewContext().RideRequests);
    }

    [Fact]
    public async Task A_ride_from_another_university_cannot_be_requested_by_direct_post()
    {
        _test.Db.AddUniversity(TestData.OtherUniversity);
        var foreignDriver = _test.Db.AddUser("U2024003", TestData.OtherUniversity);
        var ride = AddRide(driver: foreignDriver, universityCode: TestData.OtherUniversity);

        var controller = Controller(_passenger);
        await controller.RequestRide(ride.Id, "Gate A", null, null);

        Assert.Equal("This ride doesn't belong to your university.", controller.TempData["Error"]);
        Assert.Empty(_test.NewContext().RideRequests);
    }

    [Fact]
    public async Task A_full_ride_cannot_be_requested()
    {
        var ride = AddRide(availableSeats: 0);
        var controller = Controller(_passenger);

        await controller.RequestRide(ride.Id, "Gate A", null, null);

        Assert.Equal("This ride is no longer available.", controller.TempData["Error"]);
    }

    [Fact]
    public async Task A_ride_that_has_set_off_cannot_be_requested()
    {
        var ride = AddRide(tripStartedAt: DateTime.Now.AddMinutes(-5));
        var controller = Controller(_passenger);

        await controller.RequestRide(ride.Id, "Gate A", null, null);

        Assert.Equal("This ride has already started and can no longer accept requests.",
            controller.TempData["Error"]);
    }

    [Fact]
    public async Task Requests_are_rate_limited_per_university_settings()
    {
        // Edge case "excessive ride requests". The limit is configurable rather
        // than hardcoded, so the test sets it low and proves the setting is read.
        _test.Db.UniversitySettings.Add(new UniversitySettings
        {
            UniversityCode = TestData.DefaultUniversity,
            MaxRideRequestsPerWindow = 2,
            RideRequestWindowMinutes = 10
        });
        _test.Db.SaveChanges();

        for (var i = 0; i < 2; i++)
        {
            var ride = AddRide();
            await Controller(_passenger).RequestRide(ride.Id, "Gate A", null, null);
        }

        var third = AddRide();
        var controller = Controller(_passenger);
        await controller.RequestRide(third.Id, "Gate A", null, null);

        Assert.Contains("sent a lot of ride requests", (string)controller.TempData["Error"]!);
        Assert.Equal(2, _test.NewContext().RideRequests.Count());
    }

    [Fact]
    public async Task Older_requests_fall_out_of_the_rate_limit_window()
    {
        // Proves the limit is a rolling window, not a lifetime cap.
        _test.Db.UniversitySettings.Add(new UniversitySettings
        {
            UniversityCode = TestData.DefaultUniversity,
            MaxRideRequestsPerWindow = 1,
            RideRequestWindowMinutes = 10
        });
        _test.Db.SaveChanges();

        var stale = AddRide();
        _test.Db.RideRequests.Add(new RideRequest
        {
            RideId = stale.Id,
            PassengerId = _passenger.Id,
            PickupLocation = "Old stop",
            RequestedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        _test.Db.SaveChanges();

        var fresh = AddRide();
        var controller = Controller(_passenger);
        await controller.RequestRide(fresh.Id, "Gate A", null, null);

        Assert.Null(controller.TempData["Error"]);
        Assert.Equal(2, _test.NewContext().RideRequests.Count());
    }

    private sealed class StubGeocoder : IGeocodingService
    {
        public Task<(double lat, double lng)?> GeocodeAsync(string address)
            => Task.FromResult<(double, double)?>((33.8938, 35.5018));

        public Task<string?> ReverseGeocodeAsync(double lat, double lng)
            => Task.FromResult<string?>("Beirut");
    }
}
