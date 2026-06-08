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

        [Required]
        [StringLength(50)]
        [Display(Name = "Vehicle Type")]
        public string VehicleType { get; set; } = string.Empty;

        [Range(1, 8)]
        [Display(Name = "Total Seats")]
        public int TotalSeats { get; set; } = 3;

        // Updated automatically as requests are accepted (FR-15)
        [Display(Name = "Available Seats")]
        public int AvailableSeats { get; set; }

        public RideStatus Status { get; set; } = RideStatus.Active;

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
    }
}
