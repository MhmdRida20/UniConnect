namespace UniConnect.Models
{
    /// <summary>
    /// Whether a specific university has a specific service turned on — the
    /// actual per-university on/off switch (Services.docx: "Per-university
    /// service enablement and configuration"). One row per (university,
    /// service) pair. No row = not enabled (defaults closed, not open).
    ///
    /// There's no admin UI to toggle this yet (that belongs to the future web
    /// portal for admins) — for now these rows are set via DbSeeder. The
    /// enforcement logic (RequireServiceAttribute, nav-bar filtering) is
    /// fully real and works the same regardless of how the row got there.
    /// </summary>
    public class UniversityService
    {
        public int Id { get; set; }

        public string UniversityCode { get; set; } = string.Empty;
        public virtual University? University { get; set; }

        public string ServiceCode { get; set; } = string.Empty;
        public virtual Service? Service { get; set; }

        public bool IsEnabled { get; set; } = true;
    }
}
