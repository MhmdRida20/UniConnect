using System.ComponentModel.DataAnnotations;

namespace UniConnect.Models
{
    /// <summary>
    /// A message posted inside a study group (FR-22). Only approved members can post.
    /// </summary>
    public class StudyGroupMessage
    {
        public int Id { get; set; }

        public int StudyGroupId { get; set; }
        public virtual StudyGroup? StudyGroup { get; set; }

        [Required]
        public string SenderId { get; set; } = string.Empty;
        public virtual ApplicationUser? Sender { get; set; }

        [Required]
        [StringLength(1000)]
        public string Content { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }
}
