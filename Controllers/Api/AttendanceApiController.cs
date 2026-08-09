using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers.Api
{
    [ApiController]
    [Route("api/attendance")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Student")]
    public class AttendanceApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AttendanceSubmissionService _submissionService;
        private readonly IHubContext<AttendanceHub> _hub;

        public AttendanceApiController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            AttendanceSubmissionService submissionService,
            IHubContext<AttendanceHub> hub)
        {
            _db = db;
            _userManager = userManager;
            _submissionService = submissionService;
            _hub = hub;
        }

        public class SessionInfoResponse
        {
            public bool Found { get; set; }
            public string? Error { get; set; }
            public string? CourseCode { get; set; }
            public string? CourseName { get; set; }
            public DateTime? StartTime { get; set; }
            public DateTime? EndTime { get; set; }
        }

        // Called right after a QR scan (or manual token entry) so the app can
        // show "CSC301 — Data Structures, ready to check in" before actually
        // submitting GPS + committing anything — same up-front info
        // AttendanceController.ScanSubmit shows on the web page. Deliberately
        // doesn't touch the database beyond a read — no side effects here.
        [HttpGet("session-info")]
        public async Task<IActionResult> SessionInfo([FromQuery] string token)
        {
            var session = await _db.AttendanceSessions.FirstOrDefaultAsync(s => s.QrToken == token);
            if (session is null)
                return Ok(new SessionInfoResponse { Found = false, Error = "This attendance code isn't valid." });

            string? error = session.Status switch
            {
                AttendanceSessionStatus.Active when DateTime.Now < session.StartTime => "This session hasn't started yet.",
                AttendanceSessionStatus.Active when DateTime.Now > session.QrExpiresAt => "This QR code has expired.",
                AttendanceSessionStatus.Active => null,
                _ => "This session is no longer active."
            };

            return Ok(new SessionInfoResponse
            {
                Found = true,
                Error = error,
                CourseCode = session.CourseCode,
                CourseName = session.CourseName,
                StartTime = session.StartTime,
                EndTime = session.EndTime
            });
        }

        public class SubmitRequest
        {
            public string Token { get; set; } = string.Empty;
            public double? Lat { get; set; }
            public double? Lng { get; set; }
            public string? DeviceFingerprint { get; set; }
        }

        public class SubmitResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        [HttpPost("submit")]
        public async Task<IActionResult> Submit(SubmitRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var (ok, message, record) = await _submissionService.TrySubmitAsync(
                user, request.Token, request.Lat, request.Lng, request.DeviceFingerprint);

            if (ok && record is not null)
            {
                // Same live update the web instructor dashboard already
                // gets — a mobile check-in shows up on the instructor's
                // screen in real time too, not just the other way around.
                await _hub.Clients.Group($"attendance-session-{record.AttendanceSessionId}").SendAsync("RosterUpdated");
            }

            return Ok(new SubmitResponse { Success = ok, Message = message });
        }

        public class AttendanceHistoryEntry
        {
            public string CourseName { get; set; } = string.Empty;
            public DateTime SessionStartTime { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        [HttpGet("history")]
        public async Task<IActionResult> History()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var records = await _db.AttendanceRecords
                .Include(r => r.Session)
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.Session!.StartTime)
                .Select(r => new AttendanceHistoryEntry
                {
                    CourseName = r.Session!.CourseName,
                    SessionStartTime = r.Session!.StartTime,
                    Status = r.Status.ToString()
                })
                .ToListAsync();

            return Ok(records);
        }
    }
}
