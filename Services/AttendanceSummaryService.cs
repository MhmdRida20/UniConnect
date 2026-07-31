using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.ViewModels;

namespace UniConnect.Services
{
    /// <summary>
    /// Aggregates a course's attendance into the shape both the instructor
    /// dashboard and its CSV export render. Deliberately one code path: the
    /// page and the download must never disagree about a student's rate.
    ///
    /// Two rules drive everything here, and both come from how the data is
    /// actually written rather than from preference:
    ///
    /// 1. ONLY CLOSED SESSIONS COUNT. An AttendanceRecord exists only for a
    ///    student who submitted; the Absent rows are backfilled in bulk when
    ///    the instructor closes the session (InstructorAttendanceController
    ///    .CloseSession). An in-progress session therefore contains nothing
    ///    but attenders, and including it would silently inflate every rate
    ///    on the page. Cancelled sessions never happened, so they're out too.
    ///
    /// 2. UNREGISTERED STUDENTS ARE NOT ZERO. CloseSession can only create
    ///    Absent records for roster entries it can match to an ApplicationUser.
    ///    A student who never signed up has no records whatsoever, which is
    ///    indistinguishable from perfect absence unless it's called out —
    ///    so they get their own standing and are kept out of every average.
    /// </summary>
    public class AttendanceSummaryService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUniversityProviderResolver _providerResolver;

        // Promote to UniversitySettings if these ever need to differ per
        // institution — there's no attendance-threshold column today.
        public const int GoodThreshold = 85;
        public const int WatchThreshold = 75;

        public AttendanceSummaryService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IUniversityProviderResolver providerResolver)
        {
            _db = db;
            _userManager = userManager;
            _providerResolver = providerResolver;
        }

        /// <summary>
        /// Courses this instructor teaches, each with the session counts drawn
        /// from one grouped query. No per-course API call — that would be a
        /// round trip per card.
        /// </summary>
        public async Task<List<InstructorCourseListItemVM>> GetCourseListAsync(ApplicationUser instructor)
        {
            var provider = await _providerResolver.GetProviderAsync(instructor.UniversityCode);
            var courses = await provider.GetTaughtCoursesAsync(instructor.UniversityCode, instructor.UniversityId);
            if (courses.Count == 0) return new List<InstructorCourseListItemVM>();

            var codes = courses.Select(c => c.CourseCode).ToList();

            var stats = await _db.AttendanceSessions
                .Where(s => s.InstructorId == instructor.Id && codes.Contains(s.CourseCode))
                .GroupBy(s => s.CourseCode)
                .Select(g => new
                {
                    CourseCode = g.Key,
                    Held = g.Count(s => s.Status == AttendanceSessionStatus.Closed),
                    Active = g.Count(s => s.Status == AttendanceSessionStatus.Active),
                    Last = (DateTime?)g.Max(s => s.StartTime)
                })
                .ToListAsync();

            return courses
                .Select(c =>
                {
                    var stat = stats.FirstOrDefault(s => s.CourseCode == c.CourseCode);
                    return new InstructorCourseListItemVM
                    {
                        CourseCode = c.CourseCode,
                        CourseName = c.CourseName,
                        Credits = c.Credits,
                        SessionsHeld = stat?.Held ?? 0,
                        ActiveSessions = stat?.Active ?? 0,
                        LastSessionAt = stat?.Last
                    };
                })
                .OrderBy(c => c.CourseName)
                .ToList();
        }

        /// <summary>
        /// The full dashboard for one course. Returns null when the instructor
        /// isn't assigned to it — the caller turns that into a 403. This check
        /// is load-bearing: the roster lookup below is keyed only by course
        /// code, so without it any instructor could read any course's students.
        /// </summary>
        public async Task<CourseAttendanceSummaryVM?> BuildCourseSummaryAsync(ApplicationUser instructor, string courseCode)
        {
            if (string.IsNullOrWhiteSpace(courseCode)) return null;

            var provider = await _providerResolver.GetProviderAsync(instructor.UniversityCode);
            var taught = await provider.GetTaughtCoursesAsync(instructor.UniversityCode, instructor.UniversityId);
            var course = taught.FirstOrDefault(c =>
                string.Equals(c.CourseCode, courseCode, StringComparison.OrdinalIgnoreCase));
            if (course is null) return null;

            var roster = await provider.GetEnrolledStudentsAsync(instructor.UniversityCode, course.CourseCode);

            var sessions = await _db.AttendanceSessions
                .Include(s => s.Records)
                .Where(s => s.InstructorId == instructor.Id && s.CourseCode == course.CourseCode)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            var closed = sessions.Where(s => s.Status == AttendanceSessionStatus.Closed).ToList();

            // Roster entries matched to UniConnect accounts. Everything
            // measurable hangs off this join.
            var studentNumbers = roster.Select(r => r.StudentNumber).ToList();
            var accounts = await _userManager.Users
                .Where(u => u.UniversityCode == instructor.UniversityCode && studentNumbers.Contains(u.UniversityId))
                .Select(u => new { u.Id, u.UniversityId })
                .ToListAsync();

            // Flatten once rather than scanning every session per student.
            var recordsByUser = closed
                .SelectMany(s => s.Records)
                .GroupBy(r => r.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var vm = new CourseAttendanceSummaryVM
            {
                CourseCode = course.CourseCode,
                CourseName = course.CourseName,
                Credits = course.Credits,
                InstructorName = course.InstructorName,
                EnrolledCount = roster.Count,
                RegisteredCount = accounts.Count,
                SessionsHeld = closed.Count,
                ActiveSessions = sessions.Count(s => s.Status == AttendanceSessionStatus.Active),
                CancelledSessions = sessions.Count(s => s.Status == AttendanceSessionStatus.Cancelled),
                FirstSessionAt = closed.FirstOrDefault()?.StartTime,
                LastSessionAt = closed.LastOrDefault()?.StartTime,
                GoodThreshold = GoodThreshold,
                WatchThreshold = WatchThreshold
            };

            foreach (var student in roster.OrderBy(r => r.FullName))
            {
                var account = accounts.FirstOrDefault(a => a.UniversityId == student.StudentNumber);

                var row = new StudentAttendanceRowVM
                {
                    StudentNumber = student.StudentNumber,
                    FullName = student.FullName,
                    Email = student.UniversityEmail,
                    HasAccount = account is not null
                };

                if (account is null)
                {
                    row.Standing = AttendanceStanding.NotRegistered;
                    vm.Students.Add(row);
                    continue;
                }

                var records = recordsByUser.TryGetValue(account.Id, out var found) ? found : new List<AttendanceRecord>();

                row.Present = records.Count(r => r.Status == AttendanceStatus.Present);
                row.Late = records.Count(r => r.Status == AttendanceStatus.Late);
                row.Absent = records.Count(r => r.Status == AttendanceStatus.Absent);
                row.Excused = records.Count(r => r.Status == AttendanceStatus.Excused);
                row.SuspiciousCount = records.Count(r => r.IsSuspicious);

                // Excused sessions leave the denominator entirely — an approved
                // absence shouldn't damage a student's rate.
                //
                // The denominator is the course's closed-session count, not the
                // student's record count, so a session they have no row for
                // still counts against them. Those two are equal in the normal
                // case; they diverge only when someone registers after a
                // session has already been closed.
                row.EligibleSessions = Math.Max(0, closed.Count - row.Excused);
                row.Rate = row.EligibleSessions > 0
                    ? Math.Round(row.Attended * 100.0 / row.EligibleSessions, 1)
                    : null;

                row.LastAttendedAt = records
                    .Where(r => r.SubmittedAt.HasValue &&
                                (r.Status == AttendanceStatus.Present || r.Status == AttendanceStatus.Late))
                    .Max(r => r.SubmittedAt);

                row.Standing = row.Rate switch
                {
                    null => AttendanceStanding.Good,      // nothing held yet — not a judgement
                    >= GoodThreshold => AttendanceStanding.Good,
                    >= WatchThreshold => AttendanceStanding.Watch,
                    _ => AttendanceStanding.AtRisk
                };

                vm.Students.Add(row);
            }

            // Worst first — the page exists to surface the students in trouble.
            // Unregistered rows sink to the bottom: they're an admin problem,
            // not an attendance one.
            vm.Students = vm.Students
                .OrderBy(s => s.HasAccount ? 0 : 1)
                .ThenBy(s => s.Rate ?? double.MaxValue)
                .ThenBy(s => s.FullName)
                .ToList();

            var tracked = vm.Students.Where(s => s.HasAccount).ToList();
            var totalEligible = tracked.Sum(s => s.EligibleSessions);

            vm.OverallRate = totalEligible > 0
                ? Math.Round(tracked.Sum(s => s.Attended) * 100.0 / totalEligible, 1)
                : null;

            vm.AtRiskCount = tracked.Count(s => s.Standing == AttendanceStanding.AtRisk);
            vm.SuspiciousCount = tracked.Sum(s => s.SuspiciousCount);

            foreach (var session in closed)
            {
                var attended = session.Records.Count(r =>
                    r.Status == AttendanceStatus.Present || r.Status == AttendanceStatus.Late);

                vm.Trend.Add(new SessionTrendPointVM
                {
                    SessionId = session.Id,
                    Date = session.StartTime,
                    Attended = attended,
                    Total = accounts.Count,
                    Rate = accounts.Count > 0 ? Math.Round(attended * 100.0 / accounts.Count, 1) : 0
                });
            }

            vm.AvgAttendedPerSession = vm.Trend.Count > 0
                ? Math.Round(vm.Trend.Average(t => t.Attended), 1)
                : null;

            return vm;
        }
    }
}
