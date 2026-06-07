using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum RideRequestStatus
    {
        Pending = 0,    // waiting for driver decision
        Accepted = 1,   // driver accepted (seat reserved)
        Rejected = 2,   // driver rejected
        Cancelled = 3   // student cancelled their own request
    }

    /// <summary>
    /// A request by a passenger student to join a ride (UC-04, FR-10, FR-11).
    /// </summary>
    public class RideRequest
    {
        public int Id { get; set; }

        public int RideId { get; set; }
        public virtual Ride? Ride { get; set; }

        // The student requesting the seat (FK to ApplicationUser.Id)
        [Required]
        public string PassengerId { get; set; } = string.Empty;
        public virtual ApplicationUser? Passenger { get; set; }

        // Where the driver should pick this passenger up (FR-11)
        [Required]
        [StringLength(150)]
        [Display(Name = "Pickup Location")]
        public string PickupLocation { get; set; } = string.Empty;

        public RideRequestStatus Status { get; set; } = RideRequestStatus.Pending;

        [Display(Name = "Requested On")]
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }
}
