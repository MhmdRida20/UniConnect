using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// A university's own internship-posting account (formerly a
    /// self-registering global "real company" account — that model is
    /// gone). Exactly ONE of these is created automatically whenever an
    /// admin creates a university (see AdminUniversitiesController.Create),
    /// using a real career-services email the admin provides. Every
    /// internship it posts is always ON BEHALF OF a real external employer
    /// — see Internship.cs's ExternalEmployerName/etc.
    /// </summary>
    public class Company
    {
        public int Id { get; set; }

        // The ApplicationUser account that owns/manages this posting account.
        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        // Which university this posting account belongs to — required now;
        // there is exactly one Company per University.
        [Required]
        [StringLength(20)]
        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        [Required]
        [StringLength(150)]
        public string CompanyName { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Required]
        [StringLength(150)]
        [EmailAddress]
        public string ContactEmail { get; set; } = string.Empty;

        [StringLength(300)]
        public string? LogoPath { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<Internship> Internships { get; set; } = new List<Internship>();
    }
}
