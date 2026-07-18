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
        //
        // NOTE ON NAMING: this is the student's ID NUMBER (matches
        // Student.UniversityId), not which university they attend — see
        // UniversityCode below for that. Kept as-is; see the same note on
        // Student.cs for why it wasn't renamed as part of this refactor.
        [Required]
        [StringLength(20)]
        [Display(Name = "University ID")]
        public string UniversityId { get; set; } = string.Empty;

        // Which university this account belongs to (the actual tenant key
        // used to pick an IUniversityProvider implementation for this user).
        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        // Only meaningful for accounts in the "DepartmentStaff" role — which
        // department they handle tickets for (matches TicketCategory.Name,
        // e.g. "IT", "Registration"). Null for students.
        [StringLength(50)]
        public string? Department { get; set; }

        // Auth Edge Cases: "Account suspended during active session" — when
        // true, SuspendedUserMiddleware signs the user out on their very
        // next request, regardless of how much of their auth cookie's
        // lifetime remains.
        public bool IsSuspended { get; set; } = false;

        // Navigation: study groups this user has joined
        public virtual ICollection<StudyGroupMember> StudyGroupMemberships { get; set; }
            = new List<StudyGroupMember>();

        // Navigation: study groups created by this user
        public virtual ICollection<StudyGroup> CreatedStudyGroups { get; set; }
            = new List<StudyGroup>();
    }
}
