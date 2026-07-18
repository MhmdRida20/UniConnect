using UniConnect.Models;

namespace UniConnect.ViewModels
{
    /// <summary>
    /// One row in an instructor's live session roster — combines the
    /// adapter's enrolled-student list with this session's AttendanceRecord
    /// (if the student has one yet), since those come from two different
    /// sources (academic data vs. UniConnect's own attendance data).
    /// </summary>
    public class AttendanceRosterRow
    {
        public string StudentNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? UserId { get; set; } // null if this student never registered an account
        public AttendanceRecord? Record { get; set; }
    }
}
