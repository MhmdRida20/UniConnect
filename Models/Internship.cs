using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    public enum InternshipApplicationStatus { Submitted, UnderReview, Shortlisted, Accepted, Rejected, Withdrawn }

    /// <summary>
    /// Which of the two paths this posting uses. The university's own
    /// posting account (Company.cs) ALWAYS posts on behalf of a real
    /// external employer — there is no path where the posting account
    /// itself is the employer.
    ///   ListingOnly     — no in-app applications at all. Students are
    ///                     pointed to the real employer's own apply link
    ///                     and/or apply email instead.
    ///   FullApplication — students apply in-app (matching score, cover
    ///                     message, status tracking); the university
    ///                     reviews everyone and forwards the best
    ///                     candidates to the real employer by email (see
    ///                     CompanyController.SendShortlist).
    /// </summary>
    public enum InternshipPostingMode { ListingOnly, FullApplication }

    /// <summary>
    /// An internship opportunity, always posted by a university's own
    /// posting account (Company.cs) on behalf of a real external employer —
    /// see ExternalEmployerName/etc. below, which are required regardless
    /// of PostingMode.
    /// </summary>
    public class Internship
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public virtual Company? Company { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        // Comma-separated (e.g. "C#, SQL, Communication") — simple and
        // matches the ER diagram's own description of this field. Parsed
        // into a list by MatchingScoreService for comparison. Not used for
        // matching under ListingOnly (no applications to score), but still
        // shown to students as information about the role.
        [StringLength(500)]
        public string? RequiredSkills { get; set; }

        // Comma-separated course codes (e.g. "CSC301, CSC340").
        [StringLength(500)]
        public string? RecommendedCourses { get; set; }

        // Comma-separated majors this posting is relevant for (e.g.
        // "Computer Science, Information Systems"). Null/empty means open
        // to all majors — never penalizes a student's match score, and
        // never blocks anyone from applying; it only affects the score and
        // the optional "my major" browse filter. See MatchingScoreService.
        [StringLength(500)]
        public string? RelevantMajors { get; set; }

        [Required]
        [StringLength(150)]
        public string Location { get; set; } = string.Empty;

        public int? DurationWeeks { get; set; }

        [Required]
        public DateTime ApplicationDeadline { get; set; }

        public int NumberOfPositions { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public InternshipPostingMode PostingMode { get; set; } = InternshipPostingMode.FullApplication;

        // ---- The real employer this listing is actually for — ALWAYS
        // required, in both modes, since the posting account never IS the
        // employer itself.
        [Required]
        [StringLength(150)]
        public string ExternalEmployerName { get; set; } = string.Empty;

        [StringLength(150)]
        [EmailAddress]
        public string? ExternalEmployerContactEmail { get; set; }

        [StringLength(300)]
        public string? ExternalEmployerLogoPath { get; set; }

        // ---- Only used/required when PostingMode == ListingOnly. At
        // least one of the two must be provided — a student needs SOME way
        // to actually reach the employer.
        [StringLength(500)]
        public string? ExternalApplyUrl { get; set; }

        [StringLength(150)]
        [EmailAddress]
        public string? ExternalApplyEmail { get; set; }

        // Set whenever the university sends the current Shortlisted
        // applicants to ExternalEmployerContactEmail (see
        // CompanyController.SendShortlist). Only meaningful for
        // FullApplication postings — the real employer normally has no
        // UniConnect login of their own, so "forwarding candidates" means
        // emailing them the shortlist directly, not granting dashboard access.
        public DateTime? ShortlistSentAt { get; set; }

        public virtual ICollection<InternshipApplication> Applications { get; set; } = new List<InternshipApplication>();
    }

    /// <summary>
    /// A student's application to an internship (FR-42, FR-43). Unique per
    /// (InternshipId, UserId) — Edge Case: "Duplicate application."
    /// </summary>
    public class InternshipApplication
    {
        public int Id { get; set; }

        public int InternshipId { get; set; }
        public virtual Internship? Internship { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public virtual ApplicationUser? User { get; set; }

        [StringLength(1000)]
        public string? CoverMessage { get; set; }

        // Frozen at the moment of application (0-100) — see
        // MatchingScoreService. Browsing shows a LIVE score that updates as
        // the student's profile changes; this one is the historical record
        // of what it was when they actually applied.
        public int? MatchingScore { get; set; }

        public InternshipApplicationStatus Status { get; set; } = InternshipApplicationStatus.Submitted;

        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Set the moment THIS specific application is included in a
        // "Send Shortlist to Employer" email (CompanyController.SendShortlist).
        // Tracked per-application (not just once on the Internship) so that
        // if more candidates get shortlisted later, a follow-up send only
        // includes the new ones — nobody gets forwarded to the employer twice.
        public DateTime? SentToEmployerAt { get; set; }
    }
}
