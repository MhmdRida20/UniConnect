using System.ComponentModel.DataAnnotations;

namespace UniConnect.ViewModels
{
    public class VehicleCreateVM
    {
        [Required(ErrorMessage = "Please select a vehicle type.")]
        [StringLength(50)]
        [Display(Name = "Vehicle Type")]
        public string VehicleType { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the plate number.")]
        [StringLength(20)]
        [Display(Name = "Plate Number")]
        public string PlateNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the vehicle's color.")]
        [StringLength(30)]
        public string Color { get; set; } = string.Empty;

        [Range(1, 8, ErrorMessage = "Seat capacity must be between 1 and 8.")]
        [Display(Name = "Seat Capacity")]
        public int SeatCapacity { get; set; } = 4;
    }
}
