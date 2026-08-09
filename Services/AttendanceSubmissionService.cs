using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// The FR-21 attendance validation rules (enrollment, time window, token
    /// validity, GPS radius, duplicate submissions, device reuse) in one
    /// place — extracted from AttendanceController so both the web submit
    /// flow (QR scan / manual entry pages) and the mobile API call the exact
    /// same logic instead of two copies that could quietly drift apart.
    /// See AttendanceController's class-level comment for the honest scope
    /// note on what device/location integrity signals aren't implementable
    /// from a browser — that reasoning applies here too, though the mobile
    /// app (a native app, not a browser) is a better position to eventually
    /// add the ones the web comment calls out as impossible for a website.
    /// </summary>
    public class AttendanceSubmissionService
    {
        private readonly ApplicationDbContext _db;
        private readonly IUniversityProviderResolver _providerResolver;
        private readonly AuditLogService _auditLog;

        public AttendanceSubmissionService(
            ApplicationDbContext db,
            IUniversityProviderResolver providerResolver,
            AuditLogService auditLog)
        {
            _db = db;
            _providerResolver = providerResolver;
            _auditLog = auditLog;
        }

        public async Task<(bool Ok, string Message, AttendanceRecord? Record)> TrySubmitAsync(
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
                // Local, not UtcNow — see the original comment in
                // AttendanceController: every other time on this entity
                // graph (session StartTime/EndTime, `now` above) is local,
                // so this one has to match or Present-vs-Late compares
                // against a value hours adrift.
                SubmittedAt = now,
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
        public static double HaversineDistanceMeters(double lat1, double lng1, double lat2, double lng2)
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
