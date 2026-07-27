using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// A department staff member's record — local storage synced from a
    /// university's external API, mirroring Student.cs and Instructor.cs
    /// exactly, for the same reason: verifying "is this person actually on
    /// staff at this university, in this department?" during self-registration.
    ///
    /// Named StaffRecord (not "Staff") to avoid any confusion with the
    /// "DepartmentStaff" ApplicationUser role — this is the pre-account
    /// academic-adapter-side record, same relationship Student.cs has to
    /// student ApplicationUser accounts.
    /// </summary>
    public class StaffRecord
    {
        [Key]
        [StringLength(20)]
        public string StaffId { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        [Required]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [EmailAddress]
        public string UniversityEmail { get; set; } = string.Empty;

        // Verified as part of the record itself, not self-declared at
        // registration — a staff member can't claim a different department
        // than the one their university's own records assign them to.
        [Required]
        [StringLength(50)]
        public string Department { get; set; } = string.Empty;
    }
}
