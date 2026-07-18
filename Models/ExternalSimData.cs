using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// Persisted storage for the simulated external university API's OWN
    /// data — deliberately separate from UniConnect's own Student/Course/
    /// Enrollment tables (which are the local CACHE that gets synced FROM
    /// this data, not the same thing). Everything here is keyed by API key,
    /// since that's what identifies "which external university" a row
    /// belongs to — the same way a real integration partner would keep
    /// their own database, unrelated to ours.
    ///
    /// This used to live only in memory (a singleton dictionary), which
    /// meant it was wiped every time the app restarted even though the
    /// University row referencing it stayed behind. Persisting it here
    /// fixes that: the exact same students/courses survive restarts.
    /// </summary>
    public class ExternalSimCourse
    {
        [Required]
        [StringLength(100)]
        public string ApiKey { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        public string CourseCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string CourseName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? InstructorName { get; set; }

        [StringLength(20)]
        public string? InstructorStaffId { get; set; }

        public int Credits { get; set; }
    }

    public class ExternalSimStudent
    {
        [Required]
        [StringLength(100)]
        public string ApiKey { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string StudentNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Email { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Major { get; set; }

        public int YearOfStudy { get; set; }
    }

    public class ExternalSimEnrollment
    {
        [Required]
        [StringLength(100)]
        public string ApiKey { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string StudentNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        public string CourseCode { get; set; } = string.Empty;
    }
}
