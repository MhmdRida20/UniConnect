using UniConnect.Models;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;
using UniConnect.ViewModels;

namespace UniConnect.Tests.Unit;

/// <summary>
/// The instructor course dashboard's aggregation.
///
/// Worth testing carefully because every number here is a judgement about a
/// student — "at risk" appears on screen next to their name — and because the
/// two rules the service is built on (only closed sessions count; unregistered
/// students are not zero) are invisible in the output when they're working and
/// silently wrong when they aren't.
/// </summary>
public class AttendanceSummaryServiceTests : IDisposable
{
    private const string Course = "CSC301";

    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();
    private readonly ApplicationUser _instructor;

    public AttendanceSummaryServiceTests()
    {
        _test.Db.AddUniversity();
        _instructor = _test.Db.AddUser("STAFF01", fullName: "Dr Habib");
        _provider.WithCourse(Course, "Data Structures", "Dr Habib")
                 .Teaches(_instructor.UniversityId, Course);
    }

    public void Dispose() => _test.Dispose();

    private AttendanceSummaryService Service() =>
        new(_test.Db, IdentityHarness.CreateUserManager(_test.Db), _provider);

    /// <summary>Adds a roster student who also holds a UniConnect account.</summary>
    private ApplicationUser RegisteredStudent(string number, string name)
    {
        _provider.WithStudent(number, name).Enroll(number, Course);
        return _test.Db.AddUser(number, fullName: name);
    }

    // ---------- BuildCourseSummaryAsync: access control ----------

    [Fact]
    public async Task Returns_null_for_a_course_this_instructor_does_not_teach()
    {
        // Load-bearing: the roster lookup below it is keyed only by course code,
        // so without this check any instructor could read any course's students.
        var other = _test.Db.AddUser("STAFF02");

        var summary = await Service().BuildCourseSummaryAsync(other, Course);

        Assert.Null(summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOPE404")]
    public async Task Returns_null_for_a_missing_or_unknown_course(string courseCode)
    {
        Assert.Null(await Service().BuildCourseSummaryAsync(_instructor, courseCode));
    }

    // ---------- Rule 1: only closed sessions count ----------

    [Fact]
    public async Task An_active_session_is_excluded_from_the_denominator()
    {
        // An in-progress session holds records only for students who have
        // already checked in, so counting it would inflate everyone's rate.
        var student = RegisteredStudent("U2024001", "Ali");

        var closed = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        var active = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Active);
        _test.Db.AddRecord(closed, student, AttendanceStatus.Present);
        _test.Db.AddRecord(active, student, AttendanceStatus.Present);

        var summary = await Service().BuildCourseSummaryAsync(_instructor, Course);

        Assert.Equal(1, summary!.SessionsHeld);
        Assert.Equal(1, summary.ActiveSessions);

        var row = summary.Students.Single();
        Assert.Equal(1, row.EligibleSessions);
        Assert.Equal(1, row.Present);       // only the closed session's record
        Assert.Equal(100, row.Rate);
    }

    [Fact]
    public async Task A_cancelled_session_never_happened_and_is_excluded()
    {
        var student = RegisteredStudent("U2024001", "Ali");
        _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Cancelled);

        var summary = await Service().BuildCourseSummaryAsync(_instructor, Course);

        Assert.Equal(0, summary!.SessionsHeld);
        Assert.Equal(1, summary.CancelledSessions);
        Assert.Equal(0, summary.Students.Single().EligibleSessions);
    }

    [Fact]
    public async Task A_session_with_no_record_at_all_still_counts_against_the_student()
    {
        // The denominator is the course's closed-session count, not the count of
        // the student's own rows — otherwise someone who simply never submitted
        // would show 100%.
        var student = RegisteredStudent("U2024001", "Ali");

        var attended = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);   // no row for them
        _test.Db.AddRecord(attended, student, AttendanceStatus.Present);

        var row = (await Service().BuildCourseSummaryAsync(_instructor, Course))!.Students.Single();

        Assert.Equal(2, row.EligibleSessions);
        Assert.Equal(50, row.Rate);
    }

    // ---------- Excused ----------

    [Fact]
    public async Task An_excused_absence_leaves_the_denominator_entirely()
    {
        // An approved absence must not damage a rate.
        var student = RegisteredStudent("U2024001", "Ali");

        var present = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        var excused = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddRecord(present, student, AttendanceStatus.Present);
        _test.Db.AddRecord(excused, student, AttendanceStatus.Excused);

        var row = (await Service().BuildCourseSummaryAsync(_instructor, Course))!.Students.Single();

        Assert.Equal(1, row.Excused);
        Assert.Equal(1, row.EligibleSessions);   // 2 held, 1 excused
        Assert.Equal(100, row.Rate);
        Assert.Equal(AttendanceStanding.Good, row.Standing);
    }

    // ---------- Rule 2: unregistered students are not zero ----------

    [Fact]
    public async Task A_roster_student_with_no_account_gets_its_own_standing()
    {
        // They have no records whatsoever, which is indistinguishable from
        // perfect absence unless it is called out separately.
        RegisteredStudent("U2024001", "Ali");
        _provider.WithStudent("U2024099", "Never Signed Up").Enroll("U2024099", Course);

        var session = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddRecord(session, _test.Db.Users.First(u => u.UniversityId == "U2024001"),
            AttendanceStatus.Present);

        var summary = await Service().BuildCourseSummaryAsync(_instructor, Course);

        Assert.Equal(2, summary!.EnrolledCount);
        Assert.Equal(1, summary.RegisteredCount);

        var ghost = summary.Students.Single(s => s.StudentNumber == "U2024099");
        Assert.False(ghost.HasAccount);
        Assert.Equal(AttendanceStanding.NotRegistered, ghost.Standing);
        Assert.Null(ghost.Rate);

        // …and they must not drag the course average down.
        Assert.Equal(100, summary.OverallRate);
        Assert.Equal(0, summary.AtRiskCount);
    }

    // ---------- Standing thresholds ----------

    [Fact]
    public async Task Standing_boundaries_sit_exactly_on_85_and_75()
    {
        // Twenty sessions so the boundaries land on whole percentages.
        var good = RegisteredStudent("U2024001", "Exactly Good");     // 17/20 = 85.0
        var watch = RegisteredStudent("U2024002", "Exactly Watch");   // 15/20 = 75.0
        var risk = RegisteredStudent("U2024003", "Just Below");       // 14/20 = 70.0

        var sessions = Enumerable.Range(0, 20)
            .Select(_ => _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed))
            .ToList();

        void Attend(ApplicationUser student, int count)
        {
            for (var i = 0; i < sessions.Count; i++)
                _test.Db.AddRecord(sessions[i], student,
                    i < count ? AttendanceStatus.Present : AttendanceStatus.Absent);
        }

        Attend(good, 17);
        Attend(watch, 15);
        Attend(risk, 14);

        var summary = await Service().BuildCourseSummaryAsync(_instructor, Course);
        var rows = summary!.Students.ToDictionary(s => s.StudentNumber);

        Assert.Equal(85, rows["U2024001"].Rate);
        Assert.Equal(AttendanceStanding.Good, rows["U2024001"].Standing);

        Assert.Equal(75, rows["U2024002"].Rate);
        Assert.Equal(AttendanceStanding.Watch, rows["U2024002"].Standing);

        Assert.Equal(70, rows["U2024003"].Rate);
        Assert.Equal(AttendanceStanding.AtRisk, rows["U2024003"].Standing);

        Assert.Equal(1, summary.AtRiskCount);
    }

    [Fact]
    public async Task Late_counts_as_attended()
    {
        var student = RegisteredStudent("U2024001", "Ali");
        var session = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddRecord(session, student, AttendanceStatus.Late);

        var row = (await Service().BuildCourseSummaryAsync(_instructor, Course))!.Students.Single();

        Assert.Equal(1, row.Late);
        Assert.Equal(1, row.Attended);
        Assert.Equal(100, row.Rate);
    }

    [Fact]
    public async Task Before_any_session_is_held_nobody_is_judged()
    {
        // No rate yet is not the same as a bad rate.
        RegisteredStudent("U2024001", "Ali");

        var summary = await Service().BuildCourseSummaryAsync(_instructor, Course);
        var row = summary!.Students.Single();

        Assert.Null(row.Rate);
        Assert.Equal(AttendanceStanding.Good, row.Standing);
        Assert.Null(summary.OverallRate);
        Assert.False(summary.HasData);
    }

    // ---------- Ordering ----------

    [Fact]
    public async Task Students_are_ordered_worst_first_with_unregistered_last()
    {
        // The page exists to surface people in trouble; an admin-side gap
        // (no account) sinks to the bottom because it isn't an attendance
        // problem.
        var strong = RegisteredStudent("U2024001", "Strong");
        var weak = RegisteredStudent("U2024002", "Weak");
        _provider.WithStudent("U2024099", "No Account").Enroll("U2024099", Course);

        var a = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        var b = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddRecord(a, strong, AttendanceStatus.Present);
        _test.Db.AddRecord(b, strong, AttendanceStatus.Present);
        _test.Db.AddRecord(a, weak, AttendanceStatus.Absent);
        _test.Db.AddRecord(b, weak, AttendanceStatus.Absent);

        var summary = await Service().BuildCourseSummaryAsync(_instructor, Course);

        Assert.Equal(
            new[] { "U2024002", "U2024001", "U2024099" },
            summary!.Students.Select(s => s.StudentNumber));
    }

    // ---------- Trend + aggregates ----------

    [Fact]
    public async Task Trend_reports_one_point_per_closed_session()
    {
        var one = RegisteredStudent("U2024001", "Ali");
        var two = RegisteredStudent("U2024002", "Sara");

        var first = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        var second = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Active);   // ignored

        _test.Db.AddRecord(first, one, AttendanceStatus.Present);
        _test.Db.AddRecord(first, two, AttendanceStatus.Present);
        _test.Db.AddRecord(second, one, AttendanceStatus.Present);
        _test.Db.AddRecord(second, two, AttendanceStatus.Absent);

        var summary = await Service().BuildCourseSummaryAsync(_instructor, Course);

        Assert.Equal(2, summary!.Trend.Count);
        Assert.Equal(100, summary.Trend[0].Rate);
        Assert.Equal(50, summary.Trend[1].Rate);
        Assert.Equal(1.5, summary.AvgAttendedPerSession);
        Assert.Equal(75, summary.OverallRate);
    }

    [Fact]
    public async Task Suspicious_submissions_are_surfaced_per_student_and_per_course()
    {
        var student = RegisteredStudent("U2024001", "Ali");
        var session = _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddRecord(session, student, AttendanceStatus.Present, suspicious: true);

        var summary = await Service().BuildCourseSummaryAsync(_instructor, Course);

        Assert.Equal(1, summary!.SuspiciousCount);
        Assert.Equal(1, summary.Students.Single().SuspiciousCount);
    }

    // ---------- GetCourseListAsync ----------

    [Fact]
    public async Task Course_list_reports_held_and_active_counts_per_course()
    {
        _provider.WithCourse("MAT202", "Discrete Maths").Teaches(_instructor.UniversityId, "MAT202");

        _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Closed);
        _test.Db.AddSession(_instructor.Id, Course, status: AttendanceSessionStatus.Active);
        _test.Db.AddSession(_instructor.Id, "MAT202", status: AttendanceSessionStatus.Closed);

        var list = await Service().GetCourseListAsync(_instructor);

        Assert.Equal(2, list.Count);

        var csc = list.Single(c => c.CourseCode == Course);
        Assert.Equal(2, csc.SessionsHeld);
        Assert.Equal(1, csc.ActiveSessions);

        var mat = list.Single(c => c.CourseCode == "MAT202");
        Assert.Equal(1, mat.SessionsHeld);
        Assert.Equal(0, mat.ActiveSessions);
    }

    [Fact]
    public async Task Course_list_is_empty_when_the_instructor_teaches_nothing()
    {
        var idle = _test.Db.AddUser("STAFF03");

        Assert.Empty(await Service().GetCourseListAsync(idle));
    }

    [Fact]
    public async Task Course_list_does_not_query_the_registrar_once_per_course()
    {
        // One round trip per card would make the page's cost scale with the
        // instructor's teaching load.
        _provider.WithCourse("MAT202").Teaches(_instructor.UniversityId, "MAT202");
        _provider.WithCourse("PHY201").Teaches(_instructor.UniversityId, "PHY201");

        await Service().GetCourseListAsync(_instructor);

        Assert.Equal(1, _provider.CallCount);
    }
}
