using System.ComponentModel.DataAnnotations;
using UniConnect.Models;

namespace UniConnect.ViewModels
{
    /// <summary>
    /// Payload for saving a whole batch of skill edits at once.
    ///
    /// Adding skills used to POST per skill, which meant a full page reload
    /// (and a scroll back to the top of the page) after every single one.
    /// The Skills card now stages changes client-side and submits them
    /// together, so the reload happens once.
    /// </summary>
    public class SkillsBatchVM
    {
        /// <summary>Skills staged for creation, in the order the student added them.</summary>
        public List<PendingSkillVM> NewSkills { get; set; } = new();

        /// <summary>Ids of already-saved skills staged for deletion.</summary>
        public List<int> RemovedIds { get; set; } = new();
    }

    public class PendingSkillVM
    {
        [StringLength(100)]
        public string? Name { get; set; }

        public SkillProficiency? Level { get; set; }
    }
}
