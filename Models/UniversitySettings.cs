using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// FR-11: "The system shall allow each university to configure the
    /// settings of its enabled services." One row per university —
    /// auto-created with sensible defaults whenever a university is
    /// created (see AdminUniversitiesController.Create and DbSeeder).
    ///
    /// These are genuinely enforced, not just cosmetic defaults — see the
    /// specific enforcement points noted on each field below.
    /// </summary>
    public class UniversitySettings
    {
        [Key]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        // Enforced in StudyGroupsController.Create — a student can set a
        // lower MaxMembers for their own group, but never higher than this.
        [Range(2, 100)]
        public int MaxStudyGroupMembers { get; set; } = 10;

        // Pre-filled as the default when an instructor creates a new
        // Attendance session (AttendanceController.Create GET) — the
        // instructor can still adjust it per-session, this just sets what
        // they see first.
        [Range(5, 1000)]
        public int DefaultAttendanceGpsRadiusMeters { get; set; } = 100;

        [Range(1, 60)]
        public int DefaultAttendanceGraceMinutes { get; set; } = 10;

        // Enforced in ClubsController.Create/EditClub — null means no cap
        // (any positive number a student sets is allowed); a value here is
        // a hard ceiling nobody can exceed regardless of what they type.
        [Range(2, 1000)]
        public int? MaxClubMembers { get; set; }

        // Enforced in RidesController.RequestRide — replaces what used to
        // be a hardcoded "5 requests per 10 minutes" for every university.
        [Range(1, 50)]
        public int MaxRideRequestsPerWindow { get; set; } = 5;

        [Range(1, 120)]
        public int RideRequestWindowMinutes { get; set; } = 10;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
