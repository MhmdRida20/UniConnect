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
    }
}
