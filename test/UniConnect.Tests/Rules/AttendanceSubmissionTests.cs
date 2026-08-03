using Microsoft.EntityFrameworkCore;
using UniConnect.Controllers;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// FR-21 / FR-23 — every rule a student's attendance submission has to clear.
///
/// Exercised through the public Submit action rather than the private method
/// that holds the logic (TEST_PLAN.md §2.2③), so what's covered is the actual
/// entry point a phone hits after scanning a QR code, including the TempData
/// message the student is shown.
///
/// Times are expressed relative to now because the application reads the clock
/// directly; see TEST_PLAN.md §2.2② for why that's a known limitation rather
/// than a preference.
/// </summary>
public class AttendanceSubmissionTests : IDisposable
{
    private const string Course = "CSC301";
    private const string ClassroomLat = "33.8938";

    // Roughly 1.1 km north of the classroom — comfortably outside any sane radius.
    private const double FarAwayLat = 33.9038;

    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();
    private readonly StubHubContext<AttendanceHub> _hub = new();
    private readonly ApplicationUser _instructor;
    private readonly ApplicationUser _student;

    public AttendanceSubmissionTests()
    {
        _test.Db.AddUniversity();
        _instructor = _test.Db.AddUser("STAFF01");
        _student = _test.Db.AddUser("U2024001");
        _provider.WithCourse(Course).Enroll(_student.UniversityId, Course);
    }

    public void Dispose() => _test.Dispose();

    private AttendanceController Controller() =>
        new AttendanceController(
                _test.Db,
                IdentityHarness.CreateUserManager(_test.Db),
                _provider,
                _hub,
                ServiceHarness.AuditLog(_test.Db))
            .SignedInAs(_student, "Student");

    private async Task<(bool Success, string Message)> Submit(
        AttendanceController controller, string token,
        double? lat = 33.8938, double? lng = 35.5018, string? device = "device-a")
    {
        await controller.Submit(token, lat, lng, device);
        return ((bool)controller.TempData["AttendanceSuccess"]!,
                (string)controller.TempData["AttendanceOutcome"]!);
    }

    // ---------- The happy path ----------

    [Fact]
    public async Task A_valid_submission_inside_the_grace_period_is_marked_Present()
    {
        var session = _test.Db.AddSession(_instructor.Id, Course, startsInMinutes: -5, graceMinutes: 10);

        var (success, message) = await Submit(Controller(), session.QrToken);

        Assert.True(success);
        Assert.Contains("Present", message);

        var record = await _test.NewContext().AttendanceRecords.SingleAsync();
        Assert.Equal(AttendanceStatus.Present, record.Status);
        Assert.Equal(_student.Id, record.UserId);
        Assert.NotNull(record.SubmittedAt);
        Assert.False(record.IsSuspicious);
    }

    [Fact]
    public async Task Submitting_after_the_grace_period_is_marked_Late()
    {
        // Started 20 minutes ago with a 10-minute grace window.
        var session = _test.Db.AddSession(_instructor.Id, Course, startsInMinutes: -20, graceMinutes: 10);

        var (success, message) = await Submit(Controller(), session.QrToken);

        Assert.True(success);
        Assert.Contains("Late", message);
        Assert.Equal(AttendanceStatus.Late, (await _test.NewContext().AttendanceRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task The_submitted_time_is_stored_in_the_same_frame_as_the_session()
    {
        // Regression guard. SubmittedAt used to be UtcNow while StartTime and
        // EndTime are local, which rendered check-ins hours adrift of the
        // session they belonged to on any machine offset from UTC.
        var session = _test.Db.AddSession(_instructor.Id, Course, startsInMinutes: -5);

        await Submit(Controller(), session.QrToken);

        var record = await _test.NewContext().AttendanceRecords.SingleAsync();
        var drift = record.SubmittedAt!.Value - session.StartTime;

        Assert.InRange(drift, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
    }

    [Fact]
    public async Task A_successful_submission_pushes_the_instructors_live_roster()
    {
        var session = _test.Db.AddSession(_instructor.Id, Course);

        await Submit(Controller(), session.QrToken);

        Assert.True(_hub.SentTo($"attendance-session-{session.Id}", "RosterUpdated"));
    }

    [Fact]
    public async Task A_successful_submission_is_audited()
    {
        var session = _test.Db.AddSession(_instructor.Id, Course);

        await Submit(Controller(), session.QrToken);

        var audit = await _test.NewContext().AuditLogs.SingleAsync(a => a.Action == "AttendanceSubmitted");
        Assert.Equal(_student.Id, audit.UserId);
        Assert.Equal("AttendanceRecord", audit.EntityType);
    }

    // ---------- Rejections ----------

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        var (success, message) = await Submit(Controller(), "not-a-real-token");

        Assert.False(success);
        Assert.Contains("isn't valid", message);
        Assert.Empty(_test.NewContext().AttendanceRecords);
    }

    [Theory]
    [InlineData(AttendanceSessionStatus.Closed)]
    [InlineData(AttendanceSessionStatus.Cancelled)]
    public async Task A_session_that_is_not_active_is_rejected(AttendanceSessionStatus status)
    {
        var session = _test.Db.AddSession(_instructor.Id, Course, status: status);

        var (success, message) = await Submit(Controller(), session.QrToken);

        Assert.False(success);
        Assert.Contains("no longer active", message);
        Assert.Empty(_test.NewContext().AttendanceRecords);
    }

    [Fact]
    public async Task Submitting_before_the_session_starts_is_rejected()
    {
        var session = _test.Db.AddSession(_instructor.Id, Course, startsInMinutes: 30, endsInMinutes: 90);

        var (success, message) = await Submit(Controller(), session.QrToken);

        Assert.False(success);
        Assert.Contains("hasn't started", message);
    }

    [Fact]
    public async Task An_expired_qr_code_is_rejected()
    {
        // UC-04 E3. The session is still Active and inside its window; only the
        // QR token has aged out.
        var session = _test.Db.AddSession(
            _instructor.Id, Course, startsInMinutes: -30, endsInMinutes: 60, qrExpiresInMinutes: -1);

        var (success, message) = await Submit(Controller(), session.QrToken);

        Assert.False(success);
        Assert.Contains("expired", message);
    }

    [Fact]
    public async Task A_student_not_enrolled_in_the_course_is_rejected()
    {
        // UC-04 E4 — enrollment is verified through the registrar, not locally.
        _provider.Unenroll(_student.UniversityId, Course);
        var session = _test.Db.AddSession(_instructor.Id, Course);

        var (success, message) = await Submit(Controller(), session.QrToken);

        Assert.False(success);
        Assert.Contains("not enrolled", message);
        Assert.Empty(_test.NewContext().AttendanceRecords);
    }

    [Fact]
    public async Task A_second_submission_for_the_same_session_is_rejected()
    {
        // UC-04 E2. Also the case the unique index on (session, user) backs up,
        // which is why this suite runs on SQLite rather than the InMemory
        // provider — the latter wouldn't enforce it.
        var session = _test.Db.AddSession(_instructor.Id, Course);
        var controller = Controller();

        await Submit(controller, session.QrToken);
        var (success, message) = await Submit(controller, session.QrToken);

        Assert.False(success);
        Assert.Contains("already submitted", message);
        Assert.Equal(1, await _test.NewContext().AttendanceRecords.CountAsync());
    }

    [Theory]
    [InlineData(null, 35.5018)]
    [InlineData(33.8938, null)]
    [InlineData(null, null)]
    public async Task A_submission_without_location_is_rejected(double? lat, double? lng)
    {
        var session = _test.Db.AddSession(_instructor.Id, Course);

        var (success, message) = await Submit(Controller(), session.QrToken, lat, lng);

        Assert.False(success);
        Assert.Contains("Location access is required", message);
    }

    [Fact]
    public async Task A_submission_outside_the_gps_radius_is_rejected()
    {
        // UC-04 E1.
        var session = _test.Db.AddSession(_instructor.Id, Course, radiusMeters: 100);

        var (success, message) = await Submit(Controller(), session.QrToken, lat: FarAwayLat);

        Assert.False(success);
        Assert.Contains("outside the 100m allowed range", message);
        Assert.Empty(_test.NewContext().AttendanceRecords);
    }

    [Fact]
    public async Task Widening_the_radius_admits_a_location_that_was_previously_too_far()
    {
        // Proves the rejection above is the distance check doing its job rather
        // than some unrelated failure.
        var session = _test.Db.AddSession(_instructor.Id, Course, radiusMeters: 5000);

        var (success, _) = await Submit(Controller(), session.QrToken, lat: FarAwayLat);

        Assert.True(success);
        var record = await _test.NewContext().AttendanceRecords.SingleAsync();
        Assert.InRange(record.DistanceFromClassroom!.Value, 900, 1300);
    }

    // ---------- Suspicion, not rejection ----------

    [Fact]
    public async Task A_device_already_used_by_another_student_is_flagged_but_still_recorded()
    {
        // The edge-case spec says "flag as suspicious", not "reject" — a shared
        // phone may be legitimate, and the instructor decides.
        var session = _test.Db.AddSession(_instructor.Id, Course);
        var classmate = _test.Db.AddUser("U2024002");
        _provider.WithStudent("U2024002").Enroll("U2024002", Course);

        var first = new AttendanceController(
                _test.Db, IdentityHarness.CreateUserManager(_test.Db), _provider, _hub,
                ServiceHarness.AuditLog(_test.Db))
            .SignedInAs(classmate, "Student");
        await Submit(first, session.QrToken, device: "shared-phone");

        var (success, _) = await Submit(Controller(), session.QrToken, device: "shared-phone");

        Assert.True(success);

        var flagged = await _test.NewContext().AttendanceRecords.SingleAsync(r => r.UserId == _student.Id);
        Assert.True(flagged.IsSuspicious);
        Assert.Contains("Same device", flagged.SuspiciousReason);
    }

    [Fact]
    public async Task A_flagged_submission_raises_its_own_audit_entry()
    {
        var session = _test.Db.AddSession(_instructor.Id, Course);
        var classmate = _test.Db.AddUser("U2024002");
        _provider.WithStudent("U2024002").Enroll("U2024002", Course);

        await Submit(new AttendanceController(
                    _test.Db, IdentityHarness.CreateUserManager(_test.Db), _provider, _hub,
                    ServiceHarness.AuditLog(_test.Db))
                .SignedInAs(classmate, "Student"),
            session.QrToken, device: "shared-phone");

        await Submit(Controller(), session.QrToken, device: "shared-phone");

        Assert.True(await _test.NewContext().AuditLogs
            .AnyAsync(a => a.Action == "SuspiciousAttendanceDetected"));
    }

    [Fact]
    public async Task The_same_student_reusing_their_own_device_is_not_suspicious()
    {
        // Two sessions, one phone — the ordinary case, and it must stay quiet.
        var monday = _test.Db.AddSession(_instructor.Id, Course);
        var tuesday = _test.Db.AddSession(_instructor.Id, Course);
        var controller = Controller();

        await Submit(controller, monday.QrToken, device: "my-phone");
        await Submit(controller, tuesday.QrToken, device: "my-phone");

        Assert.All(await _test.NewContext().AttendanceRecords.ToListAsync(),
            r => Assert.False(r.IsSuspicious));
    }

    [Fact]
    public async Task A_submission_with_no_device_fingerprint_still_succeeds()
    {
        // The fingerprint is a localStorage value; a cleared browser sends none.
        var session = _test.Db.AddSession(_instructor.Id, Course);

        var (success, _) = await Submit(Controller(), session.QrToken, device: null);

        Assert.True(success);
        Assert.False((await _test.NewContext().AttendanceRecords.SingleAsync()).IsSuspicious);
    }

    // ---------- Scan landing page ----------

    [Fact]
    public async Task The_scan_page_explains_why_a_dead_link_failed()
    {
        var controller = Controller();

        await controller.ScanSubmit("nonsense");

        Assert.Equal("This attendance link isn't valid.", controller.ViewBag.Error);
    }

    [Fact]
    public async Task The_scan_page_reports_an_expired_code_before_the_student_submits()
    {
        var session = _test.Db.AddSession(
            _instructor.Id, Course, startsInMinutes: -30, qrExpiresInMinutes: -1);
        var controller = Controller();

        await controller.ScanSubmit(session.QrToken);

        Assert.Equal("This QR code has expired.", controller.ViewBag.Error);
    }
}
