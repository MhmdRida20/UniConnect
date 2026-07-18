using System.ComponentModel.DataAnnotations;

namespace UniConnect.ViewModels
{
    public class UniversityCreateVM
    {
        [Required(ErrorMessage = "Please enter a short code, e.g. AUB.")]
        [StringLength(20)]
        [Display(Name = "University Code")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the university's name.")]
        [StringLength(150)]
        [Display(Name = "University Name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "An API base URL is required.")]
        [StringLength(300)]
        [Display(Name = "API Base URL")]
        public string ApiBaseUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "Click \"Generate\" to create an API key for this university.")]
        [StringLength(100)]
        [Display(Name = "API Key")]
        public string ApiKey { get; set; } = string.Empty;

        // Used to auto-create this university's own internship-posting
        // account (Company.cs) at the same time — a real career-services
        // address the admin already knows, not a made-up one.
        [Required(ErrorMessage = "Please enter the career services department's real email address.")]
        [EmailAddress]
        [StringLength(150)]
        [Display(Name = "Career Services Email")]
        public string CareerServicesEmail { get; set; } = string.Empty;
    }
}
