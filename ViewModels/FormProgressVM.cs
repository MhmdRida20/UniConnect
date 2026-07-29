namespace UniConnect.ViewModels
{
    /// <summary>
    /// Drives the shared _FormProgress partial — the sidebar card that shows a
    /// live "x% complete" bar plus a checklist of what's still missing.
    ///
    /// Nothing here describes WHICH fields are required: that's read at runtime
    /// straight from the rendered form's validation attributes, so the bar stays
    /// correct automatically when a ViewModel's [Required] set changes. These
    /// properties only control the card's presentation.
    /// </summary>
    public class FormProgressVM
    {
        /// <summary>Card heading, e.g. "New Study Group".</summary>
        public string Title { get; set; } = "Your progress";

        /// <summary>Optional smaller line under the title, e.g. "Course group".</summary>
        public string? Subtitle { get; set; }

        /// <summary>Sprite id from _Icons.cshtml, e.g. "#i-users".</summary>
        public string Icon { get; set; } = "#i-file";

        /// <summary>
        /// Optional CSS selector for the form to watch. Only needed when the
        /// page has more than one form and the first one inside .form-main
        /// isn't the one being tracked.
        /// </summary>
        public string? FormSelector { get; set; }

        /// <summary>
        /// Track EVERY field rather than only the [Required] ones.
        ///
        /// Use this on profile-style pages where nothing is strictly required
        /// but filling more in produces a better result (a fuller career
        /// profile scores higher against internships, for example). On a
        /// normal create form leave this false, so the bar means "can I submit
        /// yet?" rather than "have I filled in every optional extra?".
        /// </summary>
        public bool TrackAllFields { get; set; }

        /// <summary>Wording above the bar — e.g. "Completion", "Profile strength".</summary>
        public string Label { get; set; } = "Completion";

        /// <summary>Shown when every tracked field is filled.</summary>
        public string CompleteMessage { get; set; } = "All required fields complete — ready to submit.";
    }
}
