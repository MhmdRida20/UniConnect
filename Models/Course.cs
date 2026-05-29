using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// A university course. Courses are pre-seeded mock data — in production they would
    /// come from the university registrar's system.
    /// </summary>
    public class Course
    {
        // CourseCode is the natural key (e.g. "CSC301", "MAT202")
        [Key]
        [StringLength(10)]
        public string CourseCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string CourseName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? InstructorName { get; set; }

        public int Credits { get; set; }

        // Navigation: students enrolled in this course
        public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();

        // Navigation: study groups created for this course
        public virtual ICollection<StudyGroup> StudyGroups { get; set; } = new List<StudyGroup>();
    }
}
