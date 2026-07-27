using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// An instructor's academic-staff record — local storage for data that
    /// ultimately comes from a university's external API, kept fresh by the
    /// periodic sync job. Mirrors Student.cs exactly, and exists for the
    /// same reason: so "is this person actually an instructor at this
    /// university?" can be verified during self-registration, the same way
    /// it already works for students — before this existed, there was no
    /// way for a real instructor to register at all; only pre-seeded or
    /// admin-generated demo accounts existed.
    ///
    /// Separate from ApplicationUser (the login account) — this record can
    /// exist before the instructor ever creates one.
    /// </summary>
    public class Instructor
    {
        // The instructor's real staff ID, issued by their university —
        // same naming convention as Student.UniversityId.
        [Key]
        [StringLength(20)]
        public string StaffId { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [EmailAddress]
        public string UniversityEmail { get; set; } = string.Empty;
    }
}
