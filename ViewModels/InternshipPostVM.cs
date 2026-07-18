using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using UniConnect.Models;

namespace UniConnect.ViewModels
{
    public class InternshipPostVM
    {
        [Required(ErrorMessage = "Please enter a title.")]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a description.")]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "Required Skills (comma-separated)")]
        public string? RequiredSkills { get; set; }

        [StringLength(500)]
        [Display(Name = "Recommended Courses (comma-separated codes)")]
        public string? RecommendedCourses { get; set; }

        [StringLength(500)]
        [Display(Name = "Relevant Majors (comma-separated, optional)")]
        public string? RelevantMajors { get; set; }

        [Required(ErrorMessage = "Please enter a location.")]
        [StringLength(150)]
        public string Location { get; set; } = string.Empty;

        [Range(1, 104)]
        [Display(Name = "Duration (weeks)")]
        public int? DurationWeeks { get; set; }

        [Required(ErrorMessage = "Please set an application deadline.")]
        [Display(Name = "Application Deadline")]
        [DataType(DataType.Date)]
        public DateTime ApplicationDeadline { get; set; } = DateTime.Today.AddMonths(1);

        [Range(1, 100)]
        [Display(Name = "Number of Positions")]
        public int NumberOfPositions { get; set; } = 1;

        [Display(Name = "How is this being posted?")]
        public InternshipPostingMode PostingMode { get; set; } = InternshipPostingMode.FullApplication;

        // Required regardless of mode — every posting is on behalf of a
        // real named employer; the university's own account never IS the
        // employer.
        [StringLength(150)]
        [Display(Name = "Employer Name")]
        public string ExternalEmployerName { get; set; } = string.Empty;

        [StringLength(150)]
        [Display(Name = "Employer Contact Email")]
        [EmailAddress]
        public string? ExternalEmployerContactEmail { get; set; }

        public IFormFile? ExternalEmployerLogo { get; set; }

        // Only used when PostingMode == ListingOnly — at least one of these
        // two must be provided (enforced in CompanyController.ValidatePostingMode,
        // since it depends on which one the university actually has).
        [StringLength(500)]
        [Display(Name = "Application Link")]
        [Url(ErrorMessage = "Please enter a valid URL, starting with https://")]
        public string? ExternalApplyUrl { get; set; }

        [StringLength(150)]
        [Display(Name = "Application Email")]
        [EmailAddress]
        public string? ExternalApplyEmail { get; set; }
    }
}
