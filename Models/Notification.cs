using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// A system-generated notification for a user — e.g. "you were removed
    /// from a study group because you're no longer enrolled in that course."
    /// Exists so a user finds out WHY something changed rather than just
    /// noticing something disappeared, whether they're online at the moment
    /// it happens (live toast, via NotificationHub) or check back later
    /// (this persisted row, shown on /Notifications).
    /// </summary>
    public class Notification
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Message { get; set; } = string.Empty;

        // Optional relative URL to send them to when they click it (e.g.
        // back to the Study Groups list). Null if there's nowhere useful to go.
        [StringLength(300)]
        public string? Link { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; } = false;
    }
}
