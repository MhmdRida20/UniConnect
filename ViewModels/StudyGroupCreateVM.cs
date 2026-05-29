using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace UniConnect.ViewModels
{
    public class StudyGroupCreateVM
    {
        [Required, StringLength(100)]
        [Display(Name = "Group Name")]
        public string GroupName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        [Display(Name = "Course")]
        public string CourseCode { get; set; } = string.Empty;

        [Range(2, 50)]
        [Display(Name = "Maximum Members")]
        public int MaxMembers { get; set; } = 10;

        [Range(2, 50)]
        [Display(Name = "Minimum Members")]
        public int MinMembers { get; set; } = 2;

        [StringLength(100)]
        [Display(Name = "Meeting Location")]
        public string? MeetingLocation { get; set; }

        // Not bound from form — used only to populate the dropdown
        public SelectList? AvailableCourses { get; set; }
    }
}
