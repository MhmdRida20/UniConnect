using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using UniConnect.Models;

namespace UniConnect.ViewModels
{
    public class ClubCreateVM
    {
        [Required(ErrorMessage = "Please enter a club name.")]
        [StringLength(100)]
        [Display(Name = "Club Name")]
        public string ClubName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        public ClubCategory Category { get; set; } = ClubCategory.Other;

        public IFormFile? Logo { get; set; }

        [Range(2, 1000, ErrorMessage = "Must be at least 2 if set.")]
        [Display(Name = "Maximum Members (optional)")]
        public int? MaxMembers { get; set; }

        // Set to true on resubmission after confirming past a duplicate-name
        // warning (Edge Case: "Duplicate club name" — UC-16 E1).
        public bool ConfirmDuplicate { get; set; }
    }
}
