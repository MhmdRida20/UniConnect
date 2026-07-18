using System.ComponentModel.DataAnnotations;

namespace UniConnect.ViewModels
{
    public class CareerProfileEditVM
    {
        [StringLength(1000)]
        [Display(Name = "Career Interests")]
        public string? CareerInterests { get; set; }

        [StringLength(1000)]
        [Display(Name = "Career Goals")]
        public string? CareerGoals { get; set; }

        [StringLength(150)]
        [Display(Name = "Preferred Location")]
        public string? PreferredLocation { get; set; }

        [StringLength(100)]
        public string? Availability { get; set; }
    }
}
