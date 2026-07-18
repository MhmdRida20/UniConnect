using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// Represents a student's academic record — local storage for data that
    /// ultimately comes from a university's external API (see
    /// RealApiUniversityProvider and ExternalUniversityDataStore). Acts as a
    /// cache, kept fresh by the periodic sync job, rather than an
    /// authoritative source of its own.
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
        //
        // NOTE ON NAMING: despite the name, this is the student's ID NUMBER
        // issued by their university (e.g. "U2024001") — NOT which university
        // they attend. That's UniversityCode below (added for multi-university
        // support). Kept as-is rather than renamed to limit the blast radius
        // of an already-large refactor; renaming this to something like
        // StudentNumber is a reasonable follow-up if there's time later.
        [Key]
        [StringLength(20)]
        public string UniversityId { get; set; } = string.Empty;

        // Which university this student belongs to (the actual tenant key).
        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

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
