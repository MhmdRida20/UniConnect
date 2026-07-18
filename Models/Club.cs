using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum ClubCategory { Academic, Sports, Cultural, Social, Technology, Other }
    public enum ClubStatus { Active, Inactive, Archived }
    public enum ClubRole { President, VicePresident, Officer, Member }
    public enum ClubMembershipStatus { Pending, Approved, Rejected, Left }

    /// <summary>
    /// A student club or organization (FR-67 through FR-76).
    /// </summary>
    public class Club
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        [Required]
        public string CreatorId { get; set; } = string.Empty;
        public virtual ApplicationUser? Creator { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Club Name")]
        public string ClubName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        public ClubCategory Category { get; set; } = ClubCategory.Other;

        // Relative path under wwwroot/uploads/clubs/, or null if no logo.
        [StringLength(300)]
        public string? LogoPath { get; set; }

        // Null = unlimited (FR-67 / ER diagram)
        [Display(Name = "Maximum Members")]
        public int? MaxMembers { get; set; }

        public ClubStatus Status { get; set; } = ClubStatus.Active;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<ClubMember> Members { get; set; } = new List<ClubMember>();
        public virtual ICollection<ClubAnnouncement> Announcements { get; set; } = new List<ClubAnnouncement>();
        public virtual ICollection<ClubEvent> Events { get; set; } = new List<ClubEvent>();
        public virtual ICollection<ClubMessage> Messages { get; set; } = new List<ClubMessage>();
    }

    /// <summary>
    /// Club membership — join requests and approved members with a role
    /// (FR-68, FR-69).
    /// </summary>
    public class ClubMember
    {
        public int Id { get; set; }

        public int ClubId { get; set; }
        public virtual Club? Club { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        public ClubRole Role { get; set; } = ClubRole.Member;

        public ClubMembershipStatus Status { get; set; } = ClubMembershipStatus.Pending;

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
