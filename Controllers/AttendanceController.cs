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
        private readonly IHubContext<AttendanceHub> _hub;
        private readonly UniConnect.Services.AttendanceSubmissionService _submissionService;

        public AttendanceController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<AttendanceHub> hub,
            UniConnect.Services.AttendanceSubmissionService submissionService)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _submissionService = submissionService;
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

            var (result, message, record) = await _submissionService.TrySubmitAsync(user, token, lat, lng, deviceFingerprint);

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

    }
}
