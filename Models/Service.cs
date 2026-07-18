using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// Fixed string codes identifying each service in the platform's catalog.
    /// Used instead of magic strings when checking/enabling services.
    ///
    /// Matches the service list in Services.docx. Not every code here has a
    /// working controller yet (e.g. Attendance, Tickets) — per that doc,
    /// unimplemented services still appear in the catalog so a university can
    /// see what's available and "request activation when new services are
    /// released." IsImplemented on the Service entity tracks that distinction.
    /// </summary>
    public static class ServiceCodes
    {
        public const string StudyGroups = "StudyGroups";
        public const string RideSharing = "RideSharing";
        public const string Attendance = "Attendance";
        public const string Tickets = "Tickets";
        public const string Internships = "Internships";
        public const string Clubs = "Clubs";
    }

    /// <summary>
    /// A catalog entry for one of the platform's value-added services (Study
    /// Groups, Ride Sharing, Attendance, etc.) — see Services.docx, "Service
    /// Extensibility": "A new service connects to the existing core modules...
    /// Services that are not yet implemented appear in the service catalog as
    /// catalog entries. Universities can see what is available and request
    /// activation when new services are released."
    ///
    /// This table is the same regardless of which universities exist — it's
    /// the platform-wide list of what COULD be enabled. Whether a specific
    /// university actually has a service turned on lives in UniversityService.
    /// </summary>
    public class Service
    {
        // Matches one of the ServiceCodes constants above.
        [Key]
        [StringLength(30)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Description { get; set; }

        [StringLength(40)]
        public string? IconClass { get; set; } // Bootstrap Icons class, for nav/catalog UI

        // Whether this service actually has working code behind it yet.
        // A university can only ENABLE a service that's implemented — the
        // rest exist purely as forward-looking catalog entries.
        public bool IsImplemented { get; set; } = false;

        public virtual ICollection<UniversityService> UniversityServices { get; set; }
            = new List<UniversityService>();
    }
}
