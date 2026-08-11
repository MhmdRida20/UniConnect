using UniConnect.Controllers;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;
using UniConnect.ViewModels;

namespace UniConnect.Tests.Rules;

/// <summary>
/// Regression coverage for the session Details roster: a roster entry with no
/// UniConnect account has no AttendanceRecord and never will (CloseSession can
/// only backfill Absent for students it can match to an account), so lumping
/// them into PendingCount made every closed session look like it was still
/// waiting on people who were never coming. Fixed at
/// InstructorAttendanceController.cs by giving them their own count, matching
/// the NotRegistered standing AttendanceSummaryService already uses on the
/// course dashboard.
/// </summary>
public class InstructorRosterTests : IDisposable
{
    private const string Course = "CSC301";

    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();
    private readonly ApplicationUser _instructor;

    public InstructorRosterTests()
    {
        _test.Db.AddUniversity();
        _instructor = _test.Db.AddUser("STAFF01");
        _provider.WithCourse(Course).Teaches(_instructor.UniversityId, Course);
    }

    public void Dispose() => _test.Dispose();

    private InstructorAttendanceController Controller() =>
        new InstructorAttendanceController(
                _test.Db,
                IdentityHarness.CreateUserManager(_test.Db),
                _provider,
                new StubHubContext<AttendanceHub>(),
                NullConfiguration.Instance,
                new UniConnect.Services.AttendanceSummaryService(
                    _test.Db, IdentityHarness.CreateUserManager(_test.Db), _provider))
            .SignedInAs(_instructor, "Instructor");

    [Fact]
    public async Task An_unregistered_roster_student_is_not_counted_as_pending()
    {
        _provider.WithStudent("U2024099", "Never Signed Up").Enroll("U2024099", Course);
        var session = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);

        await Controller().Details(session.Id);
        var controller = Controller();
        var result = await controller.Details(session.Id);

        Assert.Equal(0, (int)controller.ViewBag.PendingCount);
        Assert.Equal(1, (int)controller.ViewBag.NotRegisteredCount);
    }

    [Fact]
    public async Task A_registered_student_who_has_not_submitted_yet_is_still_pending()
    {
        // The fix must not have swallowed the ordinary case along with the
        // unregistered one — a real account with no record yet is genuinely
        // pending.
        var student = _test.Db.AddUser("U2024001");
        _provider.WithStudent("U2024001").Enroll("U2024001", Course);
        var session = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Active);

        var controller = Controller();
        await controller.Details(session.Id);

        Assert.Equal(1, (int)controller.ViewBag.PendingCount);
        Assert.Equal(0, (int)controller.ViewBag.NotRegisteredCount);
    }

    [Fact]
    public async Task A_mix_of_pending_submitted_and_unregistered_is_counted_correctly()
    {
        var submitted = _test.Db.AddUser("U2024001");
        var pending = _test.Db.AddUser("U2024002");
        _provider.WithStudent("U2024001").Enroll("U2024001", Course);
        _provider.WithStudent("U2024002").Enroll("U2024002", Course);
        _provider.WithStudent("U2024099", "Never Signed Up").Enroll("U2024099", Course);

        var session = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Active);
        _test.Db.AddRecord(session, submitted, AttendanceStatus.Present);

        var controller = Controller();
        await controller.Details(session.Id);

        Assert.Equal(1, (int)controller.ViewBag.PresentCount);
        Assert.Equal(1, (int)controller.ViewBag.PendingCount);
        Assert.Equal(1, (int)controller.ViewBag.NotRegisteredCount);

        var roster = (List<AttendanceRosterRow>)controller.ViewBag.Roster;
        Assert.Equal(3, roster.Count);
    }
}
