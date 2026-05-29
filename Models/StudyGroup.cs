using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum StudyGroupStatus
    {
        Active = 0,
        Full = 1,
        Archived = 2,
        Inactive = 3
    }

    /// <summary>
    /// A study group is created by a student for a specific course. Only students
    /// enrolled in that course can join (FR-19, FR-24).
    /// </summary>
    public class StudyGroup
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Group Name")]
        public string GroupName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        // The course this group studies (FK to Course)
        [Required]
        [StringLength(10)]
        [Display(Name = "Course")]
        public string CourseCode { get; set; } = string.Empty;
        public virtual Course? Course { get; set; }

        // The creator/owner of the group (FK to ApplicationUser.Id which is a GUID string)
        [Required]
        public string CreatorId { get; set; } = string.Empty;
        public virtual ApplicationUser? Creator { get; set; }

        [Display(Name = "Maximum Members")]
        [Range(2, 50)]
        public int MaxMembers { get; set; } = 10;

        [Display(Name = "Minimum Members")]
        [Range(2, 50)]
        public int MinMembers { get; set; } = 2;   // FR-20

        public StudyGroupStatus Status { get; set; } = StudyGroupStatus.Active;

        [Display(Name = "Created On")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        [Display(Name = "Meeting Location")]
        public string? MeetingLocation { get; set; }

        // Navigation: all members of this group (including creator)
        public virtual ICollection<StudyGroupMember> Members { get; set; } = new List<StudyGroupMember>();

        // Navigation: messages posted in this group
        public virtual ICollection<StudyGroupMessage> Messages { get; set; } = new List<StudyGroupMessage>();
    }
}
