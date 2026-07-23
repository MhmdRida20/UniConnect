using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum RideStatus
    {
        Active = 0,      // accepting requests (FR-13)
        Full = 1,        // no seats left
        Completed = 2,   // trip finished
        Cancelled = 3    // driver cancelled
    }

    /// <summary>
    /// A ride offered by a driver student (UC-03, FR-07).
    /// Other students can request a seat (UC-04). The driver accepts/rejects (UC-05).
    /// </summary>
    public class Ride
    {
        public int Id { get; set; }

        // Which university this ride belongs to — without this, a ride could
        // theoretically be visible/joinable across universities if two
        // students happened to share a driver/passenger relationship data
        // path. Added because Rides was the one service that never had this
        // scoping, unlike Study Groups/Clubs/Tickets.
        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        // The driver who created the ride (FK to ApplicationUser.Id)
        [Required]
        public string DriverId { get; set; } = string.Empty;
        public virtual ApplicationUser? Driver { get; set; }

        [Required]
        [StringLength(150)]
        [Display(Name = "Departure Location")]
        public string DepartureLocation { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [Display(Name = "Destination")]
        public string Destination { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Departure Time")]
        public DateTime DepartureTime { get; set; } = DateTime.Now.AddHours(1);

        // References a specific vehicle the driver registered under their
        // own account (see Vehicle.cs / VehiclesController) — no longer a
        // free-text field typed fresh each time, so a ride is always tied
        // to a real, reusable vehicle profile (plate, color, capacity).
        [Required]
        public int VehicleId { get; set; }
        public virtual Vehicle? Vehicle { get; set; }

        [Range(1, 8)]
        [Display(Name = "Total Seats")]
        public int TotalSeats { get; set; } = 3;

        // Updated automatically as requests are accepted (FR-15)
        [Display(Name = "Available Seats")]
        public int AvailableSeats { get; set; }

        public RideStatus Status { get; set; } = RideStatus.Active;

        // Edge Case: "Double seat reservation — two students request the
        // last available seat simultaneously. The system shall allow only
        // the first confirmed acceptance." Same concurrency-token mechanism
        // as StudyGroup.RowVersion.
        [Timestamp]
        public byte[]? RowVersion { get; set; }

        [Display(Name = "Created On")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(300)]
        public string? Notes { get; set; }
        // Coordinates for the map (filled by geocoding; nullable so old rows are fine).
        // Ready for a future click-picker that writes accurate values here.
        public double? DepartureLat { get; set; }
        public double? DepartureLng { get; set; }
        public double? DestinationLat { get; set; }
        public double? DestinationLng { get; set; }

        // Navigation: requests made for this ride
        public virtual ICollection<RideRequest> Requests { get; set; } = new List<RideRequest>();

        // ── Live tracking (Phase 4) ──────────────────────────────────────
        // Set when the driver hits "Start Trip". Requests are no longer
        // accepted once a trip has started (see RidesController.RequestRide).
        public DateTime? TripStartedAt { get; set; }

        // Last known driver position, updated on every location push while the
        // trip is in progress. Persisted (not just broadcast) so a passenger
        // who opens/reloads the page after the driver already started sees the
        // last known position immediately, instead of a blank map until the
        // next update arrives.
        public double? LastLat { get; set; }
        public double? LastLng { get; set; }
        public DateTime? LastLocationAt { get; set; }
    }
}
