using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// FR-92: "The system shall record important system actions for
    /// security and auditing." Admin-only access (UC-20 A1 / E1).
    /// </summary>
    public class AuditLog
    {
        public int Id { get; set; }

        // Nullable — some actions (a failed login with a bad/unknown email)
        // have no real user to attach to.
        public string? UserId { get; set; }
        public virtual ApplicationUser? User { get; set; }

        [StringLength(20)]
        public string? UniversityCode { get; set; }

        [Required]
        [StringLength(50)]
        public string Action { get; set; } = string.Empty; // "Login", "FailedLogin", "AccountSuspended", "TicketCreated", etc.

        [StringLength(50)]
        public string? EntityType { get; set; } // "User", "Ticket", "Ride", "StudyGroup", etc.

        [StringLength(50)]
        public string? EntityId { get; set; }

        [StringLength(1000)]
        public string? Details { get; set; } // free-form / JSON-ish context

        [StringLength(50)]
        public string? IpAddress { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
