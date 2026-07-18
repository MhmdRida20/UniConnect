using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum RsvpStatus { Attending, Maybe, NotAttending }

    /// <summary>Officer announcement, visible to all club members (FR-70).</summary>
    public class ClubAnnouncement
    {
        public int Id { get; set; }

        public int ClubId { get; set; }
        public virtual Club? Club { get; set; }

        [Required]
        public string AuthorId { get; set; } = string.Empty;
        public virtual ApplicationUser? Author { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(2000)]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>An event created by a club officer (FR-71, FR-72).</summary>
    public class ClubEvent
    {
        public int Id { get; set; }

        public int ClubId { get; set; }
        public virtual Club? Club { get; set; }

        [Required]
        public string CreatorId { get; set; } = string.Empty;
        public virtual ApplicationUser? Creator { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        // Combined date+time (the ER diagram lists EventDate/EventTime as two
        // fields; storing as one DateTime is equivalent and simpler to work
        // with, matching how Ride.DepartureTime is already modeled).
        [Required]
        [Display(Name = "Date & Time")]
        public DateTime EventDateTime { get; set; }

        [Required]
        [StringLength(150)]
        public string Location { get; set; } = string.Empty;

        // Null = unlimited
        [Display(Name = "Maximum Attendees")]
        public int? MaxAttendees { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<EventRsvp> Rsvps { get; set; } = new List<EventRsvp>();
    }

    /// <summary>A member's RSVP response to a club event (FR-72).</summary>
    public class EventRsvp
    {
        public int Id { get; set; }

        public int ClubEventId { get; set; }
        public virtual ClubEvent? ClubEvent { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        public RsvpStatus RsvpStatus { get; set; }

        public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>A real-time chat message within a club (FR-73).</summary>
    public class ClubMessage
    {
        public int Id { get; set; }

        public int ClubId { get; set; }
        public virtual Club? Club { get; set; }

        [Required]
        public string SenderId { get; set; } = string.Empty;
        public virtual ApplicationUser? Sender { get; set; }

        [Required]
        [StringLength(1000)]
        public string Content { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }
}
