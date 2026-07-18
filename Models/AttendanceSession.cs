using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum AttendanceSessionStatus { Active, Closed, Cancelled }
    public enum AttendanceStatus { Present, Late, Absent, Excused }

    /// <summary>
    /// A digital attendance session created by an instructor (FR-18, FR-19).
    /// CourseCode is a plain string, NOT a foreign key — see the ER diagram's
    /// note: course data comes from the adapter, not a UniConnect-owned
    /// table, since a "real API" university might not have local Course rows
    /// at all. Stored here for reference/display only.
    /// </summary>
    public class AttendanceSession
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        [Required]
        [StringLength(10)]
        public string CourseCode { get; set; } = string.Empty;

        [StringLength(150)]
        public string CourseName { get; set; } = string.Empty; // captured at creation time for display, since CourseCode isn't a FK

        [Required]
        public string InstructorId { get; set; } = string.Empty;
        public virtual ApplicationUser? Instructor { get; set; }

        [Required]
        public DateTime SessionDate { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime EndTime { get; set; }

        // Minutes after StartTime during which a submission still counts as
        // Present rather than Late.
        public int GracePeriodMinutes { get; set; } = 10;

        [Required]
        public double ClassroomLat { get; set; }

        [Required]
        public double ClassroomLng { get; set; }

        // Maximum allowed distance from the classroom, in meters (FR-18, default 100).
        public int GpsRadiusMeters { get; set; } = 100;

        [Required]
        [StringLength(64)]
        public string QrToken { get; set; } = string.Empty;

        [Required]
        public DateTime QrExpiresAt { get; set; }

        public AttendanceSessionStatus Status { get; set; } = AttendanceSessionStatus.Active;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
    }

    /// <summary>
    /// One student's attendance submission (or system-generated Absent
    /// record) for a session (FR-20 through FR-24).
    /// </summary>
    public class AttendanceRecord
    {
        public int Id { get; set; }

        public int AttendanceSessionId { get; set; }
        public virtual AttendanceSession? Session { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        public DateTime? SubmittedAt { get; set; }

        public AttendanceStatus Status { get; set; }

        public double? SubmittedLat { get; set; }
        public double? SubmittedLng { get; set; }

        // Calculated distance from the classroom, in meters, at submission time.
        public double? DistanceFromClassroom { get; set; }

        // A persisted-per-browser identifier (see ScanSubmit.js) — the web
        // equivalent of a native device fingerprint. Used to catch the same
        // physical device submitting for multiple different students.
        [StringLength(100)]
        public string? DeviceFingerprint { get; set; }

        public bool IsSuspicious { get; set; }

        [StringLength(300)]
        public string? SuspiciousReason { get; set; }
    }
}
