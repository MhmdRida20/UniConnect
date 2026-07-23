using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum VehicleStatus { Active, Inactive }

    /// <summary>
    /// FR-55: "The system shall allow a student to register as a driver
    /// student by adding vehicle information." A student can register more
    /// than one vehicle (e.g. they alternate cars) — when creating a ride,
    /// they pick which registered, ACTIVE vehicle they're using for that
    /// specific trip (see RidesController.Create), rather than typing
    /// free-text vehicle details fresh every time.
    /// </summary>
    public class Vehicle
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Vehicle Type")]
        public string VehicleType { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Display(Name = "Plate Number")]
        public string PlateNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string Color { get; set; } = string.Empty;

        [Range(1, 8)]
        [Display(Name = "Seat Capacity")]
        public int SeatCapacity { get; set; } = 4;

        public VehicleStatus Status { get; set; } = VehicleStatus.Active;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
