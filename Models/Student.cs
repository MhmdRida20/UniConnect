using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// Represents a student's academic record. This is the MOCK university database side.
    /// In a real system, this data would come from the university's external API.
    /// For now, we seed it manually so we can develop and test offline.
    ///
    /// IMPORTANT: This is separate from ApplicationUser (the login account).
    /// A Student record can exist BEFORE the student creates an account — that's how
    /// we verify "is this person actually a registered student at the university?"
    /// during signup.
    /// </summary>
    public class Student
    {
        // We use UniversityId as the primary key — it's the natural ID
        // that comes from the university records.
        [Key]
        [StringLength(20)]
        public string UniversityId { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [EmailAddress]
        public string UniversityEmail { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Major { get; set; }

        public int YearOfStudy { get; set; }

        // Navigation: courses the student is enrolled in (via the Enrollment join table)
        public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    }
}
