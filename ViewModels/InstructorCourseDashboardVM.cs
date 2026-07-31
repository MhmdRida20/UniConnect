namespace UniConnect.ViewModels
{
    /// <summary>How a student is tracking against the attendance thresholds.</summary>
    public enum AttendanceStanding
    {
        Good,
        Watch,
        AtRisk,

        /// <summary>
        /// On the university's roster but has never registered a UniConnect
        /// account. These students have no AttendanceRecord rows at all —
        /// CloseSession only backfills Absent for accounts it can find — so
        /// they'd otherwise render as 0% and look like the worst attender in
        /// the class. They're excluded from every rate and from the at-risk count.
        /// </summary>
        NotRegistered
    }

    /// <summary>One card on the course picker. Session counts come from a single
    /// grouped query, deliberately not from the university API — enrolment counts
    /// would cost one HTTP round trip per card.</summary>
    public class InstructorCourseListItemVM
    {
        public string CourseCode { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public int Credits { get; set; }
        public int SessionsHeld { get; set; }
        public int ActiveSessions { get; set; }
        public DateTime? LastSessionAt { get; set; }
    }

    /// <summary>One bar in the per-session trend strip.</summary>
    public class SessionTrendPointVM
    {
        public int SessionId { get; set; }
        public DateTime Date { get; set; }
        public int Attended { get; set; }
        public int Total { get; set; }
        public double Rate { get; set; }
    }

    public class StudentAttendanceRowVM
    {
        public string StudentNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public bool HasAccount { get; set; }

        public int Present { get; set; }
        public int Late { get; set; }
        public int Absent { get; set; }
        public int Excused { get; set; }

        /// <summary>Sessions counted against this student: all closed sessions
        /// minus the ones they were excused from.</summary>
        public int EligibleSessions { get; set; }

        public int Attended => Present + Late;

        /// <summary>Null when there is nothing to measure — no closed sessions
        /// yet, no account, or every session excused.</summary>
        public double? Rate { get; set; }

        public AttendanceStanding Standing { get; set; }
        public DateTime? LastAttendedAt { get; set; }
        public int SuspiciousCount { get; set; }
    }

    public class CourseAttendanceSummaryVM
    {
        public string CourseCode { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public int Credits { get; set; }
        public string? InstructorName { get; set; }

        /// <summary>Students on the university's roster for this course.</summary>
        public int EnrolledCount { get; set; }

        /// <summary>Of those, how many have a UniConnect account and can therefore
        /// actually be tracked.</summary>
        public int RegisteredCount { get; set; }

        /// <summary>Closed sessions only — see <see cref="Services.AttendanceSummaryService"/>
        /// for why in-progress sessions can't be counted.</summary>
        public int SessionsHeld { get; set; }
        public int ActiveSessions { get; set; }
        public int CancelledSessions { get; set; }

        public double? OverallRate { get; set; }
        public double? AvgAttendedPerSession { get; set; }
        public int AtRiskCount { get; set; }
        public int SuspiciousCount { get; set; }

        public DateTime? FirstSessionAt { get; set; }
        public DateTime? LastSessionAt { get; set; }

        public List<SessionTrendPointVM> Trend { get; set; } = new();
        public List<StudentAttendanceRowVM> Students { get; set; } = new();

        public int GoodThreshold { get; set; }
        public int WatchThreshold { get; set; }

        public bool HasData => SessionsHeld > 0;
    }
}
