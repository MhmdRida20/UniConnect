using System.ComponentModel.DataAnnotations;

namespace UniConnect.ViewModels
{
    public class ClubEventCreateVM
    {
        [Required(ErrorMessage = "Please enter an event title.")]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Please choose a date and time.")]
        [Display(Name = "Date & Time")]
        [DataType(DataType.DateTime)]
        public DateTime EventDateTime { get; set; } = DateTime.Now.AddDays(1);

        [Required(ErrorMessage = "Please enter a location.")]
        [StringLength(150)]
        public string Location { get; set; } = string.Empty;

        [Range(1, 10000, ErrorMessage = "Must be at least 1 if set.")]
        [Display(Name = "Maximum Attendees (optional)")]
        public int? MaxAttendees { get; set; }
    }
}
