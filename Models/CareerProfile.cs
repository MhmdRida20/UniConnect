using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum SkillProficiency { Beginner, Intermediate, Advanced }

    /// <summary>
    /// A student's career profile for internship matching (FR-35). One per
    /// student — created/edited via CareerProfileController.
    /// </summary>
    public class CareerProfile
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        [StringLength(1000)]
        public string? CareerInterests { get; set; }

        [StringLength(1000)]
        public string? CareerGoals { get; set; }

        [StringLength(150)]
        public string? PreferredLocation { get; set; }

        // Relative path under wwwroot/uploads/cvs/, or null if none uploaded.
        [StringLength(300)]
        public string? CvFilePath { get; set; }

        [StringLength(150)]
        public string? CvFileName { get; set; } // original filename, for display

        [StringLength(100)]
        public string? Availability { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Note: skills are looked up directly via StudentSkill.UserId
        // everywhere in the codebase (CareerProfileController,
        // MatchingScoreService) — there's deliberately no Skills navigation
        // collection here, since StudentSkill has no matching foreign key
        // back to CareerProfile, only to the user.
    }

    /// <summary>
    /// One skill on a student's career profile (FR-37). Kept as its own
    /// table (not a comma-separated field on CareerProfile) so skills can be
    /// added/edited/removed individually and matched against precisely.
    /// </summary>
    public class StudentSkill
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        [Required]
        [StringLength(100)]
        public string SkillName { get; set; } = string.Empty;

        public SkillProficiency? ProficiencyLevel { get; set; }
    }
}
