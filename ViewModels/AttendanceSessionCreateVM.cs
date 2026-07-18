using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace UniConnect.ViewModels
{
    public class AttendanceSessionCreateVM
    {
        [Required(ErrorMessage = "Please choose a course.")]
        [Display(Name = "Course")]
        public string CourseCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please set a start time.")]
        [Display(Name = "Start Time")]
        [DataType(DataType.DateTime)]
        public DateTime StartTime { get; set; } = DateTime.Now.AddMinutes(5);

        [Required(ErrorMessage = "Please set an end time.")]
        [Display(Name = "End Time")]
        [DataType(DataType.DateTime)]
        public DateTime EndTime { get; set; } = DateTime.Now.AddHours(1);

        [Range(0, 60)]
        [Display(Name = "Grace Period (minutes)")]
        public int GracePeriodMinutes { get; set; } = 10;

        [Required(ErrorMessage = "Please set the classroom location on the map.")]
        [Display(Name = "Classroom Latitude")]
        public double? ClassroomLat { get; set; }

        [Required(ErrorMessage = "Please set the classroom location on the map.")]
        [Display(Name = "Classroom Longitude")]
        public double? ClassroomLng { get; set; }

        [Range(10, 1000)]
        [Display(Name = "GPS Radius (meters)")]
        public int GpsRadiusMeters { get; set; } = 100;

        // View-only — populates the course dropdown. Never actually posted
        // back by the browser (learned this the hard way with Tickets), so
        // it must be excluded from both binding and validation.
        [BindNever]
        [ValidateNever]
        public SelectList AvailableCourses { get; set; } = default!;
    }
}
