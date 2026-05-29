using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// Extends the default IdentityUser to add university-specific information.
    /// IdentityUser already gives us: Id, UserName, Email, PasswordHash, PhoneNumber, etc.
    /// We add UniversityId so we can link a logged-in user to their courses and enrollments.
    /// </summary>
    public class ApplicationUser : IdentityUser
    {
        // The university ID is the link between the Identity account and
        // the academic records (Student, Course, Enrollment tables).
        [Required]
        [StringLength(20)]
        [Display(Name = "University ID")]
        public string UniversityId { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        // Navigation: one ApplicationUser corresponds to one Student record (academic data)
        public virtual Student? Student { get; set; }

        // Navigation: study groups this user has joined
        public virtual ICollection<StudyGroupMember> StudyGroupMemberships { get; set; }
            = new List<StudyGroupMember>();

        // Navigation: study groups created by this user
        public virtual ICollection<StudyGroup> CreatedStudyGroups { get; set; }
            = new List<StudyGroup>();
    }
}
