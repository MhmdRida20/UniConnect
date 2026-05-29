using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// Join table for the many-to-many relationship between Student and Course.
    /// A student can be enrolled in many courses; a course has many students.
    ///
    /// We make this an explicit entity (instead of a hidden join table) so we can
    /// later add fields like Semester, Grade, EnrollmentDate, etc.
    /// </summary>
    public class Enrollment
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string UniversityId { get; set; } = string.Empty;
        public virtual Student? Student { get; set; }

        [Required]
        [StringLength(10)]
        public string CourseCode { get; set; } = string.Empty;
        public virtual Course? Course { get; set; }

        [StringLength(20)]
        public string Semester { get; set; } = "Fall 2026";
    }
}
