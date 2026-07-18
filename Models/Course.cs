using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// A university course — local cache of data synced from a university's
    /// external API (see RealApiUniversityProvider), not an authoritative
    /// source of its own.
    ///
    /// Composite key of (UniversityCode, CourseCode) — NOT CourseCode alone. Each
    /// university has its own course catalog, so two universities can both have
    /// a "CSC301" without colliding or leaking into each other's data. This was
    /// a deliberate correction: an earlier version shared one global course
    /// catalog across all universities, which would have let a study group or
    /// enrollment at University A silently match University B's courses if
    /// their course codes ever happened to coincide.
    /// </summary>
    public class Course
    {
        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        // CourseCode is only unique WITHIN a university (e.g. "CSC301", "MAT202")
        [Required]
        [StringLength(10)]
        public string CourseCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string CourseName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? InstructorName { get; set; }

        // Which ApplicationUser (if any) teaches this course — used by the
        // Attendance service to determine which courses an instructor can
        // create sessions for (FR-18). Nullable because InstructorName alone
        // (a plain display string) is enough for courses without a linked
        // account yet — e.g. courses just synced for a newly-provisioned university.
        public string? InstructorId { get; set; }
        public virtual ApplicationUser? Instructor { get; set; }

        public int Credits { get; set; }

        // Navigation: students enrolled in this course
        public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();

        // Navigation: study groups created for this course
        public virtual ICollection<StudyGroup> StudyGroups { get; set; } = new List<StudyGroup>();
    }
}
