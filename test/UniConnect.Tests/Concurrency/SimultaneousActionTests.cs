using Microsoft.EntityFrameworkCore;
using UniConnect.Controllers;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Concurrency;

/// <summary>
/// The two documented "simultaneous" edge cases:
///
///   • "Double seat reservation — two students request the last available seat
///     simultaneously. The system shall allow only the first confirmed
///     acceptance."
///   • Two near-simultaneous study-group approvals must not both squeeze past
///     the capacity check.
///
/// Both rely on the [Timestamp] rowversion columns on Ride and StudyGroup,
/// which only a real SQL Server populates — see LocalDbFixture for why these
/// can't live with the rest of the suite on SQLite.
///
/// The races are simulated deterministically rather than with real threads:
/// two DbContexts each read the entity, then both write. That reproduces the
/// interleaving the rowversion exists to catch, without the flakiness of hoping
/// two tasks collide.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class SimultaneousActionTests
{
    private readonly LocalDbFixture _sql;

    public SimultaneousActionTests(LocalDbFixture sql) => _sql = sql;

    // ---------- Shared setup ----------

    private (string DriverId, int RideId, int FirstRequestId, int SecondRequestId) ArrangeRideWithOneSeat()
    {
        using var db = _sql.NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var universityCode = "U" + suffix[..6];

        db.Universities.Add(new University
        {
            Code = universityCode, Name = "Race Test University",
            ApiBaseUrl = "https://localhost/api", ApiKey = "k"
        });
        db.SaveChanges();

        ApplicationUser NewUser(string tag) => new()
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"{tag}-{suffix}@uni.edu",
            NormalizedUserName = $"{tag}-{suffix}@UNI.EDU".ToUpperInvariant(),
            Email = $"{tag}-{suffix}@uni.edu",
            NormalizedEmail = $"{tag}-{suffix}@UNI.EDU".ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            UniversityId = $"{tag}{suffix}",
            UniversityCode = universityCode,
            FullName = tag
        };

        var driver = NewUser("driver");
        var first = NewUser("first");
        var second = NewUser("second");
        db.Users.AddRange(driver, first, second);
        db.SaveChanges();

        var vehicle = new Vehicle
        {
            UserId = driver.Id, VehicleType = "Sedan",
            PlateNumber = suffix[..6], Color = "Green", SeatCapacity = 4
        };
        db.Vehicles.Add(vehicle);
        db.SaveChanges();

        var ride = new Ride
        {
            UniversityCode = universityCode,
            DriverId = driver.Id,
            VehicleId = vehicle.Id,
            DepartureLocation = "Hamra",
            Destination = "Main Gate",
            DepartureTime = DateTime.Now.AddHours(2),
            TotalSeats = 1,
            AvailableSeats = 1,          // the single contested seat
            Status = RideStatus.Active
        };
        db.Rides.Add(ride);
        db.SaveChanges();

        var requestOne = new RideRequest { RideId = ride.Id, PassengerId = first.Id, PickupLocation = "Gate A" };
        var requestTwo = new RideRequest { RideId = ride.Id, PassengerId = second.Id, PickupLocation = "Gate B" };
        db.RideRequests.AddRange(requestOne, requestTwo);
        db.SaveChanges();

        return (driver.Id, ride.Id, requestOne.Id, requestTwo.Id);
    }

    private RidesController RideController(ApplicationDbContext db, string driverId)
    {
        var driver = db.Users.Single(u => u.Id == driverId);
        return new RidesController(
                db,
                IdentityHarness.CreateUserManager(db),
                new StubGeocoder(),
                new StubHubContext<RideTrackingHub>(),
                ServiceHarness.AuditLog(db),
                ServiceHarness.Notifications(db))
            .SignedInAs(driver, "Student");
    }

    // ---------- Double seat reservation ----------

    [SkippableFact]
    public async Task Only_one_of_two_simultaneous_acceptances_takes_the_last_seat()
    {
        Skip.IfNot(_sql.Available, _sql.SkipReason);

        var (driverId, rideId, firstRequestId, secondRequestId) = ArrangeRideWithOneSeat();

        // Two independent contexts — each one loads the ride, sees one free
        // seat, and decides it can accept.
        using var contextA = _sql.NewContext();
        using var contextB = _sql.NewContext();

        var controllerA = RideController(contextA, driverId);
        var controllerB = RideController(contextB, driverId);

        // Force both to read the ride (and its rowversion) before either writes.
        await contextA.Rides.Include(r => r.Requests).FirstAsync(r => r.Id == rideId);
        await contextB.Rides.Include(r => r.Requests).FirstAsync(r => r.Id == rideId);

        await controllerA.AcceptRequest(firstRequestId);
        await controllerB.AcceptRequest(secondRequestId);

        using var verify = _sql.NewContext();
        var accepted = await verify.RideRequests
            .CountAsync(r => r.RideId == rideId && r.Status == RideRequestStatus.Accepted);
        var ride = await verify.Rides.SingleAsync(r => r.Id == rideId);

        Assert.Equal(1, accepted);                 // "only the first confirmed acceptance"
        Assert.Equal(0, ride.AvailableSeats);      // never oversold
        Assert.Equal(RideStatus.Full, ride.Status);

        // The loser is told to re-check rather than being silently dropped.
        Assert.Equal(
            "This ride changed while you were accepting that request — please check available seats and try again.",
            controllerB.TempData["Error"]);
    }

    [SkippableFact]
    public async Task Seat_count_never_goes_negative_even_under_a_race()
    {
        Skip.IfNot(_sql.Available, _sql.SkipReason);

        var (driverId, rideId, firstRequestId, secondRequestId) = ArrangeRideWithOneSeat();

        using var contextA = _sql.NewContext();
        using var contextB = _sql.NewContext();
        await contextA.Rides.Include(r => r.Requests).FirstAsync(r => r.Id == rideId);
        await contextB.Rides.Include(r => r.Requests).FirstAsync(r => r.Id == rideId);

        await RideController(contextA, driverId).AcceptRequest(firstRequestId);
        await RideController(contextB, driverId).AcceptRequest(secondRequestId);

        using var verify = _sql.NewContext();
        Assert.True((await verify.Rides.SingleAsync(r => r.Id == rideId)).AvailableSeats >= 0);
    }

    // ---------- Simultaneous study-group approvals ----------

    [SkippableFact]
    public async Task Two_simultaneous_approvals_cannot_both_pass_the_capacity_check()
    {
        Skip.IfNot(_sql.Available, _sql.SkipReason);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var universityCode = "G" + suffix[..6];
        const string course = "MAT202";

        string creatorId;
        int groupId, pendingOneId, pendingTwoId;

        using (var db = _sql.NewContext())
        {
            db.Universities.Add(new University
            {
                Code = universityCode, Name = "Race Test University",
                ApiBaseUrl = "https://localhost/api", ApiKey = "k"
            });
            db.SaveChanges();

            ApplicationUser NewUser(string tag) => new()
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"{tag}-{suffix}@uni.edu",
                NormalizedUserName = $"{tag}-{suffix}@UNI.EDU".ToUpperInvariant(),
                Email = $"{tag}-{suffix}@uni.edu",
                NormalizedEmail = $"{tag}-{suffix}@UNI.EDU".ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                UniversityId = $"{tag}{suffix}",
                UniversityCode = universityCode,
                FullName = tag
            };

            var creator = NewUser("creator");
            var applicantOne = NewUser("one");
            var applicantTwo = NewUser("two");
            db.Users.AddRange(creator, applicantOne, applicantTwo);

            db.Courses.Add(new Course
            {
                UniversityCode = universityCode, CourseCode = course,
                CourseName = "Discrete Maths", Credits = 3
            });
            db.SaveChanges();

            // Room for exactly one more member beyond the creator.
            var group = new StudyGroup
            {
                UniversityCode = universityCode,
                CourseCode = course,
                CreatorId = creator.Id,
                GroupName = "Race Crew",
                MaxMembers = 2,
                MinMembers = 2,
                Status = StudyGroupStatus.Active
            };
            db.StudyGroups.Add(group);
            db.SaveChanges();

            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                StudyGroupId = group.Id, UserId = creator.Id, Status = MembershipStatus.Approved
            });
            var one = new StudyGroupMember
            {
                StudyGroupId = group.Id, UserId = applicantOne.Id, Status = MembershipStatus.Pending
            };
            var two = new StudyGroupMember
            {
                StudyGroupId = group.Id, UserId = applicantTwo.Id, Status = MembershipStatus.Pending
            };
            db.StudyGroupMembers.AddRange(one, two);
            db.SaveChanges();

            creatorId = creator.Id;
            groupId = group.Id;
            pendingOneId = one.Id;
            pendingTwoId = two.Id;
        }

        var provider = new FakeUniversityProvider()
            .WithCourse(course)
            .Enroll($"one{suffix}", course)
            .Enroll($"two{suffix}", course);

        using var contextA = _sql.NewContext();
        using var contextB = _sql.NewContext();

        StudyGroupsController Controller(ApplicationDbContext db)
        {
            var creator = db.Users.Single(u => u.Id == creatorId);
            return new StudyGroupsController(
                    db,
                    IdentityHarness.CreateUserManager(db),
                    new StubHubContext<StudyGroupHub>(),
                    provider,
                    ServiceHarness.AuditLog(db),
                    ServiceHarness.Notifications(db),
                    ServiceHarness.StudyGroups(db, provider))
                .SignedInAs(creator, "Student");
        }

        var controllerA = Controller(contextA);
        var controllerB = Controller(contextB);

        // Both read the group while it still has a free place.
        await contextA.StudyGroups.FirstAsync(g => g.Id == groupId);
        await contextB.StudyGroups.FirstAsync(g => g.Id == groupId);

        await controllerA.ApproveMember(pendingOneId);
        await controllerB.ApproveMember(pendingTwoId);

        using var verify = _sql.NewContext();
        var approved = await verify.StudyGroupMembers
            .CountAsync(m => m.StudyGroupId == groupId && m.Status == MembershipStatus.Approved);

        Assert.Equal(2, approved);   // creator + exactly one applicant, never three

        // Which of the two guards stops the second approval depends on the
        // interleaving, and both are correct outcomes: ApproveMember re-counts
        // approved members with a fresh query, so once the first save has
        // committed the plain capacity check catches it and the rowversion
        // never has to. The concurrency token is proved separately below, for
        // the tighter interleaving where the count is read before that commit.
        var error = (string?)controllerB.TempData["Error"];
        Assert.True(
            error == "The group is already full — reject or remove someone first."
            || error == "This group changed while you were approving that request — please check the group and try again.",
            $"unexpected message: {error}");
    }

    [SkippableFact]
    public async Task A_stale_write_to_a_study_group_is_rejected_by_its_concurrency_token()
    {
        // Proves the [Timestamp] token on StudyGroup is real and live, at the
        // level it operates on. The test above shows the user-facing outcome;
        // this one shows the mechanism that backs it up when the capacity
        // re-query happens to read stale data.
        Skip.IfNot(_sql.Available, _sql.SkipReason);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var universityCode = "T" + suffix[..6];
        int groupId;

        using (var db = _sql.NewContext())
        {
            db.Universities.Add(new University
            {
                Code = universityCode, Name = "Token Test University",
                ApiBaseUrl = "https://localhost/api", ApiKey = "k"
            });
            var creator = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"tok-{suffix}@uni.edu",
                NormalizedUserName = $"TOK-{suffix}@UNI.EDU".ToUpperInvariant(),
                Email = $"tok-{suffix}@uni.edu",
                NormalizedEmail = $"TOK-{suffix}@UNI.EDU".ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                UniversityId = $"tok{suffix}",
                UniversityCode = universityCode,
                FullName = "Token Creator"
            };
            db.Users.Add(creator);
            db.Courses.Add(new Course
            {
                UniversityCode = universityCode, CourseCode = "MAT202",
                CourseName = "Discrete Maths", Credits = 3
            });
            db.SaveChanges();

            var group = new StudyGroup
            {
                UniversityCode = universityCode, CourseCode = "MAT202",
                CreatorId = creator.Id, GroupName = "Token Crew", MaxMembers = 4
            };
            db.StudyGroups.Add(group);
            db.SaveChanges();
            groupId = group.Id;
        }

        using var readerA = _sql.NewContext();
        using var readerB = _sql.NewContext();

        var copyA = await readerA.StudyGroups.SingleAsync(g => g.Id == groupId);
        var copyB = await readerB.StudyGroups.SingleAsync(g => g.Id == groupId);

        Assert.NotNull(copyA.RowVersion);   // SQL Server populated it; SQLite would not have

        copyA.Status = StudyGroupStatus.Full;
        await readerA.SaveChangesAsync();

        copyB.Status = StudyGroupStatus.Inactive;   // written against the now-stale version

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => readerB.SaveChangesAsync());
    }

    private sealed class StubGeocoder : UniConnect.Services.IGeocodingService
    {
        public Task<(double lat, double lng)?> GeocodeAsync(string address)
            => Task.FromResult<(double, double)?>((33.8938, 35.5018));

        public Task<string?> ReverseGeocodeAsync(double lat, double lng)
            => Task.FromResult<string?>("Beirut");
    }
}
