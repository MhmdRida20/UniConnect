using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Filters;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Instructor-facing side of the Smart Attendance service (UC-03):
    /// create sessions, show the QR code, watch submissions arrive live,
    /// and review/override individual records (FR-24).
    /// </summary>
    [Authorize(Roles = "Instructor")]
    [RequireService(ServiceCodes.Attendance)]
    public class InstructorAttendanceController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUniversityProviderResolver _providerResolver;
        private readonly IHubContext<AttendanceHub> _hub;
        private readonly IConfiguration _config;

        public InstructorAttendanceController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IUniversityProviderResolver providerResolver,
            IHubContext<AttendanceHub> hub,
            IConfiguration config)
        {
            _db = db;
            _userManager = userManager;
            _providerResolver = providerResolver;
            _hub = hub;
            _config = config;
        }

        // Builds the URL encoded in the QR code. By default this uses
        // whatever host/port the instructor happened to be browsing on
        // (typically "localhost" in development) — which only resolves
        // correctly on that SAME machine, not on a phone scanning the code.
        // Setting "Attendance:PublicBaseUrl" in appsettings.json (e.g. to
        // your machine's LAN IP, like "https://192.168.1.23:7253") makes the
        // QR code resolve correctly from a phone on the same network too.
        private string BuildScanUrl(string qrToken)
        {
            var configuredBase = _config["Attendance:PublicBaseUrl"]?.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(configuredBase))
                return $"{configuredBase}/Attendance/ScanSubmit?token={qrToken}";

            return Url.Action("ScanSubmit", "Attendance", new { token = qrToken }, Request.Scheme)!;
        }

        // ---------- INDEX: my sessions ------------------------------------
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var sessions = await _db.AttendanceSessions
                .Include(s => s.Records)
                .Where(s => s.InstructorId == user.Id)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();

            return View(sessions);
        }

        // ---------- CREATE (GET) ------------------------------------------
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var courses = await provider.GetTaughtCoursesAsync(user.UniversityCode, user.Id);

            // FR-11: pre-fill with this university's configured defaults —
            // the instructor can still adjust either value for this
            // specific session before submitting.
            var settings = await _db.UniversitySettings.FindAsync(user.UniversityCode);

            var vm = new AttendanceSessionCreateVM
            {
                AvailableCourses = new SelectList(courses, "CourseCode", "CourseName"),
                GpsRadiusMeters = settings?.DefaultAttendanceGpsRadiusMeters ?? 100,
                GracePeriodMinutes = settings?.DefaultAttendanceGraceMinutes ?? 10
            };
            return View(vm);
        }

        // ---------- CREATE (POST) ------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AttendanceSessionCreateVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var courses = await provider.GetTaughtCoursesAsync(user.UniversityCode, user.Id);
            var course = courses.FirstOrDefault(c => c.CourseCode == vm.CourseCode);

            if (course is null)
                ModelState.AddModelError(nameof(vm.CourseCode), "Please choose a course you're assigned to teach.");

            // UC-03 E2 — session time in the past is rejected.
            if (vm.StartTime <= DateTime.Now)
                ModelState.AddModelError(nameof(vm.StartTime), "Start time must be in the future.");
            if (vm.EndTime <= vm.StartTime)
                ModelState.AddModelError(nameof(vm.EndTime), "End time must be after the start time.");

            if (!ModelState.IsValid)
            {
                vm.AvailableCourses = new SelectList(courses, "CourseCode", "CourseName", vm.CourseCode);
                return View(vm);
            }

            var session = new AttendanceSession
            {
                UniversityCode = user.UniversityCode,
                CourseCode = vm.CourseCode,
                CourseName = course!.CourseName,
                InstructorId = user.Id,
                SessionDate = vm.StartTime.Date,
                StartTime = vm.StartTime,
                EndTime = vm.EndTime,
                GracePeriodMinutes = vm.GracePeriodMinutes,
                ClassroomLat = vm.ClassroomLat!.Value,
                ClassroomLng = vm.ClassroomLng!.Value,
                GpsRadiusMeters = vm.GpsRadiusMeters,
                QrToken = Guid.NewGuid().ToString("N"),
                QrExpiresAt = vm.EndTime,
                Status = AttendanceSessionStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            _db.AttendanceSessions.Add(session);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Attendance session created — show the QR code to your class.";
            return RedirectToAction(nameof(Details), new { id = session.Id });
        }

        // ---------- EDIT SESSION (GET) — before it starts only --------------
        // Edge Case: "Session created for wrong course — instructor
        // accidentally creates a session for the wrong course. The system
        // shall allow the instructor to cancel OR EDIT the session before
        // submissions arrive." Cancel already existed; this is the "edit" half.
        public async Task<IActionResult> EditSession(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var session = await _db.AttendanceSessions.FindAsync(id);
            if (session is null) return NotFound();
            if (session.InstructorId != user.Id) return Forbid();

            if (session.StartTime <= DateTime.Now)
            {
                TempData["Error"] = "This session has already started and can no longer be edited — cancel or close it instead.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var courses = await provider.GetTaughtCoursesAsync(user.UniversityCode, user.Id);

            var vm = new AttendanceSessionCreateVM
            {
                CourseCode = session.CourseCode,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                GracePeriodMinutes = session.GracePeriodMinutes,
                ClassroomLat = session.ClassroomLat,
                ClassroomLng = session.ClassroomLng,
                GpsRadiusMeters = session.GpsRadiusMeters,
                AvailableCourses = new SelectList(courses, "CourseCode", "CourseName", session.CourseCode)
            };
            ViewBag.SessionId = id;
            return View(vm);
        }

        // ---------- EDIT SESSION (POST) --------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSession(int id, AttendanceSessionCreateVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var session = await _db.AttendanceSessions.FindAsync(id);
            if (session is null) return NotFound();
            if (session.InstructorId != user.Id) return Forbid();

            if (session.StartTime <= DateTime.Now)
            {
                TempData["Error"] = "This session has already started and can no longer be edited.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var courses = await provider.GetTaughtCoursesAsync(user.UniversityCode, user.Id);
            var course = courses.FirstOrDefault(c => c.CourseCode == vm.CourseCode);

            if (course is null)
                ModelState.AddModelError(nameof(vm.CourseCode), "Please choose a course you're assigned to teach.");
            if (vm.StartTime <= DateTime.Now)
                ModelState.AddModelError(nameof(vm.StartTime), "Start time must be in the future.");
            if (vm.EndTime <= vm.StartTime)
                ModelState.AddModelError(nameof(vm.EndTime), "End time must be after the start time.");

            if (!ModelState.IsValid)
            {
                vm.AvailableCourses = new SelectList(courses, "CourseCode", "CourseName", vm.CourseCode);
                ViewBag.SessionId = id;
                return View(vm);
            }

            session.CourseCode = vm.CourseCode;
            session.CourseName = course!.CourseName;
            session.SessionDate = vm.StartTime.Date;
            session.StartTime = vm.StartTime;
            session.EndTime = vm.EndTime;
            session.GracePeriodMinutes = vm.GracePeriodMinutes;
            session.ClassroomLat = vm.ClassroomLat!.Value;
            session.ClassroomLng = vm.ClassroomLng!.Value;
            session.GpsRadiusMeters = vm.GpsRadiusMeters;
            session.QrExpiresAt = vm.EndTime;

            await _db.SaveChangesAsync();

            TempData["Success"] = "Session updated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- DETAILS: QR code + live roster --------------------------
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var session = await _db.AttendanceSessions
                .Include(s => s.Records).ThenInclude(r => r.User)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session is null) return NotFound();
            if (session.InstructorId != user.Id) return Forbid();

            var provider = await _providerResolver.GetProviderAsync(session.UniversityCode);
            var roster = await provider.GetEnrolledStudentsAsync(session.UniversityCode, session.CourseCode);

            // Match each enrolled student to their ApplicationUser account (if
            // they've registered one) so we can look up their AttendanceRecord.
            var studentNumbers = roster.Select(r => r.StudentNumber).ToList();
            var accounts = await _userManager.Users
                .Where(u => u.UniversityCode == session.UniversityCode && studentNumbers.Contains(u.UniversityId))
                .ToListAsync();

            var rows = roster.Select(r =>
            {
                var account = accounts.FirstOrDefault(a => a.UniversityId == r.StudentNumber);
                var record = account is not null ? session.Records.FirstOrDefault(rec => rec.UserId == account.Id) : null;
                return new AttendanceRosterRow
                {
                    StudentNumber = r.StudentNumber,
                    FullName = r.FullName,
                    UserId = account?.Id,
                    Record = record
                };
            })
            .OrderBy(r => r.FullName)
            .ToList();

            ViewBag.Roster = rows;
            ViewBag.PresentCount = rows.Count(r => r.Record?.Status == AttendanceStatus.Present);
            ViewBag.LateCount = rows.Count(r => r.Record?.Status == AttendanceStatus.Late);
            ViewBag.AbsentCount = rows.Count(r => r.Record?.Status == AttendanceStatus.Absent);
            ViewBag.SuspiciousCount = rows.Count(r => r.Record?.IsSuspicious == true);
            ViewBag.PendingCount = rows.Count(r => r.Record is null);
            ViewBag.ScanUrl = BuildScanUrl(session.QrToken);

            return View(session);
        }

        // ---------- CANCEL SESSION (before it starts) — UC-03 A2 -----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelSession(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var session = await _db.AttendanceSessions.FindAsync(id);
            if (session is null) return NotFound();
            if (session.InstructorId != user.Id) return Forbid();

            if (session.StartTime <= DateTime.Now)
            {
                TempData["Error"] = "This session has already started and can't be cancelled — you can close it instead.";
                return RedirectToAction(nameof(Details), new { id });
            }

            session.Status = AttendanceSessionStatus.Cancelled;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"attendance-session-{id}").SendAsync("SessionClosed");

            TempData["Success"] = "Session cancelled.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- CLOSE SESSION EARLY --------------------------------------
        // Same effect as the background job closing it at EndTime, just
        // triggered manually by the instructor (e.g. class ended early).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseSession(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var session = await _db.AttendanceSessions.FindAsync(id);
            if (session is null) return NotFound();
            if (session.InstructorId != user.Id) return Forbid();
            if (session.Status != AttendanceSessionStatus.Active)
            {
                return RedirectToAction(nameof(Details), new { id });
            }

            var provider = await _providerResolver.GetProviderAsync(session.UniversityCode);
            var roster = await provider.GetEnrolledStudentsAsync(session.UniversityCode, session.CourseCode);
            var studentNumbers = roster.Select(r => r.StudentNumber).ToList();

            var accounts = await _userManager.Users
                .Where(u => u.UniversityCode == session.UniversityCode && studentNumbers.Contains(u.UniversityId))
                .ToListAsync();

            var submittedUserIds = await _db.AttendanceRecords
                .Where(r => r.AttendanceSessionId == id)
                .Select(r => r.UserId)
                .ToListAsync();

            foreach (var account in accounts)
            {
                if (submittedUserIds.Contains(account.Id)) continue;
                _db.AttendanceRecords.Add(new AttendanceRecord
                {
                    AttendanceSessionId = id,
                    UserId = account.Id,
                    Status = AttendanceStatus.Absent,
                    SubmittedAt = null
                });
            }

            session.Status = AttendanceSessionStatus.Closed;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"attendance-session-{id}").SendAsync("SessionClosed");

            TempData["Success"] = "Session closed. Non-submitters were marked Absent.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- OVERRIDE STATUS (FR-24) -----------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OverrideStatus(int recordId, AttendanceStatus newStatus)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var record = await _db.AttendanceRecords.Include(r => r.Session).FirstOrDefaultAsync(r => r.Id == recordId);
            if (record?.Session is null) return NotFound();
            if (record.Session.InstructorId != user.Id) return Forbid();

            record.Status = newStatus;
            // Manual overrides are, by definition, no longer suspicious —
            // the instructor has made a deliberate, informed decision.
            record.IsSuspicious = false;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"attendance-session-{record.Session.Id}").SendAsync("RosterUpdated");

            TempData["Success"] = "Attendance record updated.";
            return RedirectToAction(nameof(Details), new { id = record.Session.Id });
        }
    }
}
