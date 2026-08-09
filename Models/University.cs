using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// A university/institution using UniConnect. This is the "tenant" in the
    /// platform's multi-university design (see Services.docx: "UniConnect
    /// defines one contract; each university plugs in behind it").
    ///
    /// Every university integrates through a real IUniversityProvider
    /// (RealApiUniversityProvider) that calls its API over HTTP — there is
    /// deliberately no "mock" mode. Since no real university partner exists
    /// for this project, ApiBaseUrl points at a simulated external API
    /// (see /ExternalApi and /Controllers/ExternalApi) that behaves like a
    /// genuine third-party system: its own independent data per API key,
    /// its own authentication, its own occasional simulated instability.
    /// </summary>
    public class University
    {
        // Short natural key, e.g. "AUB", "LAU" — used everywhere else as the
        // tenant identifier instead of a numeric surrogate ID.
        [Key]
        [StringLength(20)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(300)]
        public string ApiBaseUrl { get; set; } = string.Empty;

        // Credential sent as the X-Api-Key header on every call to this
        // university's API — simulates each university issuing UniConnect
        // its own API credentials, same as any real third-party integration.
        // Also the key that selects THIS university's independent dataset
        // in the simulated external API — see ExternalUniversityDataStore.
        [Required]
        [StringLength(100)]
        public string ApiKey { get; set; } = string.Empty;

        // Populated by UniversityApiSyncService (the periodic "cron job")
        // every time it checks/syncs this university's API — gives admins
        // visibility into whether the integration is actually healthy,
        // rather than silently trusting it works.
        public DateTime? LastSyncAt { get; set; }

        [StringLength(20)]
        public string? LastSyncStatus { get; set; } // "Success" | "Failed" | null (never synced)

        [StringLength(300)]
        public string? LastSyncError { get; set; }

        public bool IsActive { get; set; } = true;

        // Which adapter shape this university's API actually speaks.
        // Null or "Simulated" = matches ExternalUniversityApiController's
        // shape exactly — every university this project provisions itself
        // (via AdminUniversitiesController.Create + "Generate") uses this.
        // "Ums" = the specific real read-only API a partner university sent
        // us (see Adapters/UmsApiUniversityProvider.cs) — different field
        // names, nested course sections, no bulk student list. Add a new
        // value here + a new IUniversityProvider implementation for any
        // future real partner whose API shape doesn't match either.
        //
        // Nullable deliberately — every university row that already existed
        // before this field was added has no value for it, and the
        // resolver already treats "anything other than exactly 'Ums'" as
        // the simulated default, so null is a perfectly safe, correct value
        // here rather than something that needs backfilling.
        [StringLength(20)]
        public string? ApiStyle { get; set; }

        // Navigation: students belonging to this university
        public virtual ICollection<Student> Students { get; set; } = new List<Student>();
        public virtual ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    }
}
