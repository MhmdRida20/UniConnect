namespace UniConnect.ViewModels
{
    /// <summary>
    /// A uniform shape for any of the 8 report types (FR-84 through FR-91) —
    /// lets one view and one CSV export method handle all of them, instead
    /// of needing 8 different templates for what's fundamentally the same
    /// "summary stats + detail table" presentation.
    /// </summary>
    public class ReportResultVM
    {
        public string Title { get; set; } = string.Empty;
        public List<(string Label, string Value)> Summary { get; set; } = new();
        public List<string> ColumnHeaders { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();

        // Edge Cases: "Report generation timeout" / "Export file too large" —
        // rather than actually letting a query run unbounded, results are
        // capped; this flag drives the "narrow your filters" message.
        public bool Truncated { get; set; }
    }
}
