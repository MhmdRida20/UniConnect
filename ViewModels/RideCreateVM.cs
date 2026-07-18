using System.ComponentModel.DataAnnotations;

namespace UniConnect.ViewModels
{
    public class RideCreateVM
    {
        [Required, StringLength(150)]
        [Display(Name = "Departure Location")]
        public string DepartureLocation { get; set; } = string.Empty;

        [Required, StringLength(150)]
        [Display(Name = "Destination")]
        public string Destination { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Departure Time")]
        [DataType(DataType.DateTime)]
        public DateTime DepartureTime { get; set; } = DateTime.Now.AddHours(1);

        [Required, StringLength(50)]
        [Display(Name = "Vehicle Type")]
        public string VehicleType { get; set; } = string.Empty;

        [Range(1, 8)]
        [Display(Name = "Total Seats")]
        public int TotalSeats { get; set; } = 3;

        [StringLength(300)]
        public string? Notes { get; set; }

        // Populated by the map pin picker (Phase 2). When present, these take
        // priority over text-address geocoding, since they come directly from
        // where the user clicked — always accurate, unlike guessing from text.
        public double? DepartureLat { get; set; }
        public double? DepartureLng { get; set; }
        public double? DestinationLat { get; set; }
        public double? DestinationLng { get; set; }
    }
}
