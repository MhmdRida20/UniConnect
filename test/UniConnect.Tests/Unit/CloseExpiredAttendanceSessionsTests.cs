using Microsoft.EntityFrameworkCore;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Unit;

/// <summary>
/// Regression coverage for the UTC/local mismatch: the job compared
/// DateTime.UtcNow against AttendanceSession.EndTime, which is a local wall-clock
/// value (it comes straight from the instructor's datetime-local input, same
/// frame as every other comparison against it). On a machine east or west of
/// UTC that closed sessions early or late by the offset — and since closing is
/// what backfills the Absent records, it also shifted who ended up marked
/// absent. Fixed at Services/CloseExpiredAttendanceSessionsService.cs by
/// switching to DateTime.Now.
///
/// These run against SQLite, not the running host's clock offset, by
/// constructing sessions relative to DateTime.Now the same way AddSession does
/// — so the assertions hold regardless of what timezone the test machine is in.
/// </summary>
public class CloseExpiredAttendanceSessionsTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly StubHubContext<AttendanceHub> _hub = new();
    private readonly ApplicationUser _instructor;

    public CloseExpiredAttendanceSessionsTests()
    {
        _test.Db.AddUniversity();
        _instructor = _test.Db.AddUser("STAFF01");
    }

    public void Dispose() => _test.Dispose();

    private CloseExpiredAttendanceSessionsService Service() =>
        new(NullServiceProvider.Instance, NullConfiguration.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CloseExpiredAttendanceSessionsService>.Instance);

    [Fact]
    public async Task A_session_whose_local_end_time_has_passed_is_closed()
    {
        // Ends 1 minute ago in LOCAL time. Under the old UtcNow comparison this
        // would stay open everywhere east of UTC (EndTime, still in the future
        // when read as if it were UTC, would never be "< now").
        var session = _test.Db.AddSession(_instructor.Id, endsInMinutes: -1, status: AttendanceSessionStatus.Active);

        await Service().CloseExpiredSessionsAsync(_test.Db, _hub);

        Assert.Equal(AttendanceSessionStatus.Closed,
            (await _test.NewContext().AttendanceSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_session_whose_local_end_time_has_not_arrived_yet_stays_open()
    {
        // Under the old UtcNow comparison this would close early everywhere
        // west of UTC (EndTime, read as if it were already UTC, would appear
        // to be in the past).
        var session = _test.Db.AddSession(_instructor.Id, endsInMinutes: 30, status: AttendanceSessionStatus.Active);

        await Service().CloseExpiredSessionsAsync(_test.Db, _hub);

        Assert.Equal(AttendanceSessionStatus.Active,
            (await _test.NewContext().AttendanceSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Closing_backfills_absent_for_every_enrolled_non_submitter()
    {
        var attended = _test.Db.AddUser("U2024001");
        var ghosted = _test.Db.AddUser("U2024002");
        _test.Db.AddCourse("CSC301");
        // Enrollment carries a real FK to Students (see ApplicationDbContext),
        // not just to an ApplicationUser.
        _test.Db.AddStudentRecord(attended.UniversityId);
        _test.Db.AddStudentRecord(ghosted.UniversityId);
        _test.Db.AddEnrollment(attended.UniversityId, "CSC301");
        _test.Db.AddEnrollment(ghosted.UniversityId, "CSC301");

        var session = _test.Db.AddSession(_instructor.Id, "CSC301", endsInMinutes: -1, status: AttendanceSessionStatus.Active);
        _test.Db.AddRecord(session, attended, AttendanceStatus.Present);

        await Service().CloseExpiredSessionsAsync(_test.Db, _hub);

        using var verify = _test.NewContext();
        var ghostRecord = await verify.AttendanceRecords.SingleAsync(r => r.UserId == ghosted.Id);
        Assert.Equal(AttendanceStatus.Absent, ghostRecord.Status);
        Assert.Null(ghostRecord.SubmittedAt);
    }

    [Fact]
    public async Task An_active_session_still_in_its_window_is_left_alone_and_gets_no_backfill()
    {
        var enrolled = _test.Db.AddUser("U2024001");
        _test.Db.AddCourse("CSC301");
        _test.Db.AddStudentRecord(enrolled.UniversityId);
        _test.Db.AddEnrollment(enrolled.UniversityId, "CSC301");
        _test.Db.AddSession(_instructor.Id, "CSC301", endsInMinutes: 30, status: AttendanceSessionStatus.Active);

        await Service().CloseExpiredSessionsAsync(_test.Db, _hub);

        Assert.Empty(_test.NewContext().AttendanceRecords);
    }

    [Fact]
    public async Task Already_closed_sessions_are_not_touched_again()
    {
        var session = _test.Db.AddSession(_instructor.Id, endsInMinutes: -100, status: AttendanceSessionStatus.Closed);

        await Service().CloseExpiredSessionsAsync(_test.Db, _hub);

        // No error, and nothing re-processed — a second pass over an
        // already-closed session must be a no-op, not a duplicate backfill.
        Assert.Equal(AttendanceSessionStatus.Closed,
            (await _test.NewContext().AttendanceSessions.SingleAsync()).Status);
        Assert.Empty(_test.NewContext().AttendanceRecords);
    }

    [Fact]
    public async Task Closing_notifies_anyone_watching_the_live_roster()
    {
        var session = _test.Db.AddSession(_instructor.Id, endsInMinutes: -1, status: AttendanceSessionStatus.Active);

        await Service().CloseExpiredSessionsAsync(_test.Db, _hub);

        Assert.True(_hub.SentTo($"attendance-session-{session.Id}", "SessionClosed"));
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
