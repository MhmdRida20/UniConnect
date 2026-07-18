using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum TicketPriority { Low, Medium, High, Urgent }

    public enum TicketStatus { Open, InProgress, WaitingForStudent, Resolved, Closed, Rejected }

    /// <summary>
    /// Department categories a ticket can be routed to (FR-27). Modeled as a
    /// real table (not a fixed enum) so each university can configure its own
    /// set of departments, per the ER diagram.
    /// </summary>
    public class TicketCategory
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty; // "IT", "Registration", "Finance", etc.

        public bool IsActive { get; set; } = true;

        public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    }

    /// <summary>
    /// A student complaint or service request (FR-26 through FR-34).
    /// </summary>
    public class Ticket
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        [Required]
        public string SubmitterId { get; set; } = string.Empty;
        public virtual ApplicationUser? Submitter { get; set; }

        public int CategoryId { get; set; }
        public virtual TicketCategory? Category { get; set; }

        // Null until a staff member (or the system) assigns it.
        public string? AssignedStaffId { get; set; }
        public virtual ApplicationUser? AssignedStaff { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        public TicketPriority Priority { get; set; } = TicketPriority.Medium;

        public TicketStatus Status { get; set; } = TicketStatus.Open;

        // Relative path under wwwroot/uploads/tickets/, or null if no attachment.
        [StringLength(300)]
        public string? AttachmentPath { get; set; }

        // Original attachment file name, shown to the user instead of the
        // generated-on-disk name.
        [StringLength(260)]
        public string? AttachmentFileName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Edge Case: "Ticket stale for too long — a ticket remains in Open
        // status without a response for an extended period. The system
        // shall escalate or flag the ticket for admin/staff review."
        public bool IsEscalated { get; set; } = false;
        public DateTime? EscalatedAt { get; set; }

        // Edge Case: "Offensive content in ticket — the system shall allow
        // staff to flag and handle accordingly."
        public bool IsFlaggedOffensive { get; set; } = false;

        public virtual ICollection<TicketResponse> Responses { get; set; } = new List<TicketResponse>();
    }

    /// <summary>
    /// One entry in a ticket's history — a reply, and/or a status change
    /// (FR-30, FR-31). PreviousStatus/NewStatus are only set when this entry
    /// represents a status change (a plain reply leaves both null).
    /// </summary>
    public class TicketResponse
    {
        public int Id { get; set; }

        public int TicketId { get; set; }
        public virtual Ticket? Ticket { get; set; }

        [Required]
        public string ResponderId { get; set; } = string.Empty;
        public virtual ApplicationUser? Responder { get; set; }

        [Required]
        [StringLength(2000)]
        public string Content { get; set; } = string.Empty;

        public TicketStatus? PreviousStatus { get; set; }
        public TicketStatus? NewStatus { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
