using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Filters;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Student-facing side of the Smart Attendance service (UC-04): scan a
    /// QR code (or type the token manually), submit attendance, and see the
    /// outcome. All the FR-21 validation rules live in one shared method so
    /// both entry paths (QR scan and manual entry) enforce identical rules.
    ///
    /// Honest scope note on device/location integrity: this is a browser
    /// client, not the native MAUI app, so two of the FR-23 signals aren't
    /// implementable here:
    ///   - "Mock location provider detected" requires native OS APIs with no
    ///     browser equivalent — there is no way for a website to ask "is this
    ///     GPS reading coming from a spoofing app."
    ///   - The device fingerprint is a persisted-per-BROWSER random ID
    ///     (localStorage), not a hardware identifier — clearing browser data
    ///     or using a different browser on the same phone produces a "new"
    ///     device. A native app has much stronger device identifiers available.
    /// Everything else in FR-21/FR-23 (enrollment, time window, token
    /// validity, GPS radius, duplicate submissions, device reuse across
    /// students) is fully implemented.
    /// </summary>
    [Authorize]
    [RequireService(ServiceCodes.Attendance)]
    public class AttendanceController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUniversityProviderResolver _providerResolver;
        private readonly IHubContext<AttendanceHub> _hub;
        private readonly UniConnect.Services.AuditLogService _auditLog;

        public AttendanceController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IUniversityProviderResolver providerResolver,
            IHubContext<AttendanceHub> hub,
            UniConnect.Services.AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _providerResolver = providerResolver;
            _hub = hub;
            _auditLog = auditLog;
        }

        // ---------- SCAN LANDING PAGE (from the QR code's URL) ---------------
        public async Task<IActionResult> ScanSubmit(string token)
        {
            var session = await _db.AttendanceSessions.FirstOrDefaultAsync(s => s.QrToken == token);
            ViewBag.Token = token;

            if (session is null)
            {
                ViewBag.Error = "This attendance link isn't valid.";
                return View();
            }

            ViewBag.Session = session;

            if (session.Status != AttendanceSessionStatus.Active)
                ViewBag.Error = "This session is no longer active.";
            else if (DateTime.Now < session.StartTime)
                ViewBag.Error = "This session hasn't started yet.";
            else if (DateTime.Now > session.QrExpiresAt)
                ViewBag.Error = "This QR code has expired.";

            return View();
        }

        // ---------- MANUAL TOKEN ENTRY (UC-04 A1) -----------------------------
        public IActionResult ManualEntry() => View();

        // ---------- SUBMIT (shared by both entry paths) -----------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(string token, double? lat, double? lng, string? deviceFingerprint)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var (result, message, record) = await TrySubmitAttendanceAsync(user, token, lat, lng, deviceFingerprint);

            if (result)
            {
                await _hub.Clients.Group($"attendance-session-{record!.AttendanceSessionId}").SendAsync("RosterUpdated");
            }

            TempData["AttendanceOutcome"] = message;
            TempData["AttendanceSuccess"] = result;
            return RedirectToAction(nameof(Result));
        }

        public IActionResult Result()
        {
            ViewBag.Message = TempData["AttendanceOutcome"] as string ?? "No submission found.";
            ViewBag.Success = TempData["AttendanceSuccess"] as bool? ?? false;
            return View();
        }

        // ---------- MY ATTENDANCE (simple history/report) ----------------------
        public async Task<IActionResult> MyAttendance()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var records = await _db.AttendanceRecords
                .Include(r => r.Session)
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.Session!.StartTime)
                .ToListAsync();

            return View(records);
        }

        // ---------- Core validation (FR-21) ------------------------------------
        private async Task<(bool ok, string message, AttendanceRecord? record)> TrySubmitAttendanceAsync(
            ApplicationUser user, string token, double? lat, double? lng, string? deviceFingerprint)
        {
            var session = await _db.AttendanceSessions.FirstOrDefaultAsync(s => s.QrToken == token);
            if (session is null)
                return (false, "This attendance link isn't valid.", null);

            if (session.Status != AttendanceSessionStatus.Active)
                return (false, "This session is no longer active.", null);

            var now = DateTime.Now;
            if (now < session.StartTime)
                return (false, "This session hasn't started yet.", null);

            // UC-04 E3 — expired token/QR code.
            if (now > session.QrExpiresAt)
                return (false, "This QR code has expired.", null);

            // UC-04 E4 — enrollment verification through the adapter.
            var provider = await _providerResolver.GetProviderAsync(session.UniversityCode);
            var enrolled = await provider.IsEnrolledAsync(session.UniversityCode, user.UniversityId, session.CourseCode);
            if (!enrolled)
                return (false, "You're not enrolled in this course, so this attendance can't be recorded.", null);

            // UC-04 E2 — duplicate submission.
            var existing = await _db.AttendanceRecords.FirstOrDefaultAsync(
                r => r.AttendanceSessionId == session.Id && r.UserId == user.Id);
            if (existing is not null)
                return (false, "You've already submitted attendance for this session.", null);

            if (lat is null || lng is null)
                return (false, "Location access is required to submit attendance.", null);

            // UC-04 E1 — GPS outside the classroom radius.
            var distance = HaversineDistanceMeters(session.ClassroomLat, session.ClassroomLng, lat.Value, lng.Value);
            if (distance > session.GpsRadiusMeters)
                return (false, $"You're about {Math.Round(distance)}m from the classroom — outside the {session.GpsRadiusMeters}m allowed range.", null);

            // Status: Present if within the grace period, otherwise Late.
            var status = now <= session.StartTime.AddMinutes(session.GracePeriodMinutes)
                ? AttendanceStatus.Present
                : AttendanceStatus.Late;

            // Same-device-different-student check — flagged, not rejected,
            // per the edge cases doc ("shall flag submissions as suspicious").
            var isSuspicious = false;
            string? suspiciousReason = null;
            if (!string.IsNullOrWhiteSpace(deviceFingerprint))
            {
                var deviceUsedByOther = await _db.AttendanceRecords.AnyAsync(
                    r => r.AttendanceSessionId == session.Id
                      && r.DeviceFingerprint == deviceFingerprint
                      && r.UserId != user.Id);
                if (deviceUsedByOther)
                {
                    isSuspicious = true;
                    suspiciousReason = "Same device already used by another student for this session.";
                }
            }

            var record = new AttendanceRecord
            {
                AttendanceSessionId = session.Id,
                UserId = user.Id,
                SubmittedAt = DateTime.UtcNow,
                Status = status,
                SubmittedLat = lat,
                SubmittedLng = lng,
                DistanceFromClassroom = distance,
                DeviceFingerprint = deviceFingerprint,
                IsSuspicious = isSuspicious,
                SuspiciousReason = suspiciousReason
            };
            _db.AttendanceRecords.Add(record);
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "AttendanceSubmitted",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "AttendanceRecord",
                entityId: record.Id.ToString(),
                details: $"Session {session.Id} ({session.CourseName}), status: {status}");

            if (isSuspicious)
            {
                await _auditLog.LogAsync(
                    "SuspiciousAttendanceDetected",
                    userId: user.Id,
                    universityCode: user.UniversityCode,
                    entityType: "AttendanceRecord",
                    entityId: record.Id.ToString(),
                    details: suspiciousReason);
            }

            var friendlyStatus = status == AttendanceStatus.Present ? "Present" : "Late";
            return (true, $"Attendance recorded — marked {friendlyStatus} for {session.CourseName}.", record);
        }

        // Standard great-circle distance formula, accurate enough for
        // classroom-scale GPS radius checks.
        private static double HaversineDistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            const double earthRadiusMeters = 6371000;
            var dLat = ToRadians(lat2 - lat1);
            var dLng = ToRadians(lng2 - lng1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusMeters * c;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    }
}
