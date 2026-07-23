using System.ComponentModel.DataAnnotations;

namespace UniConnect.ViewModels
{
    public class UniversitySettingsEditVM
    {
        [Range(2, 100)]
        [Display(Name = "Max Study Group Members")]
        public int MaxStudyGroupMembers { get; set; } = 10;

        [Range(5, 1000)]
        [Display(Name = "Default Attendance GPS Radius (meters)")]
        public int DefaultAttendanceGpsRadiusMeters { get; set; } = 100;

        [Range(1, 60)]
        [Display(Name = "Default Attendance Grace Period (minutes)")]
        public int DefaultAttendanceGraceMinutes { get; set; } = 10;

        [Range(2, 1000)]
        [Display(Name = "Max Club Members (leave blank for no limit)")]
        public int? MaxClubMembers { get; set; }

        [Range(1, 50)]
        [Display(Name = "Max Ride Requests per Window")]
        public int MaxRideRequestsPerWindow { get; set; } = 5;

        [Range(1, 120)]
        [Display(Name = "Ride Request Window (minutes)")]
        public int RideRequestWindowMinutes { get; set; } = 10;
    }
}
