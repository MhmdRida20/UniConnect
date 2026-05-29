using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum MembershipStatus
    {
        Pending = 0,    // join request awaiting approval (A1 of UC-07)
        Approved = 1,
        Rejected = 2,
        Left = 3
    }

    /// <summary>
    /// Join table linking a student account to a study group they have joined or requested to join.
    /// </summary>
    public class StudyGroupMember
    {
        public int Id { get; set; }

        public int StudyGroupId { get; set; }
        public virtual StudyGroup? StudyGroup { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        public MembershipStatus Status { get; set; } = MembershipStatus.Approved;

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
