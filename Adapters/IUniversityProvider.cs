namespace UniConnect.Adapters
{
    /// <summary>
    /// The one contract UniConnect owns for reading academic data from a
    /// university. Every service (Study Groups, Attendance, etc.) depends
    /// ONLY on this interface — never directly on a database table or an
    /// external API. The application never knows, and never needs to know,
    /// implementation details for a given university.
    ///
    /// One implementation exists — RealApiUniversityProvider — which calls
    /// the university's own API over HTTP (in this project, a simulated one,
    /// since there's no real university partner; see /ExternalApi). There is
    /// deliberately no mock/local-only implementation: every university
    /// integrates through a genuine API call, including the default one
    /// (see DbSeeder for how its dataset is provisioned).
    ///
    /// All academic data is READ-ONLY from UniConnect's point of view — this
    /// interface has no Create/Update/Delete methods. The university remains
    /// the system of record for identity, courses, and enrollment.
    /// </summary>
    public interface IUniversityProvider
    {
        /// <summary>Is this student currently enrolled in this course?</summary>
        Task<bool> IsEnrolledAsync(string universityCode, string studentNumber, string courseCode);

        /// <summary>All courses this student is currently enrolled in.</summary>
        Task<List<UniversityCourseDto>> GetEnrolledCoursesAsync(string universityCode, string studentNumber);

        /// <summary>The full course catalog for this university.</summary>
        Task<List<UniversityCourseDto>> GetAllCoursesAsync(string universityCode);

        /// <summary>Basic academic profile for this student, if they exist.</summary>
        Task<UniversityStudentDto?> GetStudentInfoAsync(string universityCode, string studentNumber);

        /// <summary>Courses this instructor is assigned to teach (FR-18 precondition for creating an attendance session).</summary>
        Task<List<UniversityCourseDto>> GetTaughtCoursesAsync(string universityCode, string instructorId);

        /// <summary>Full roster of students enrolled in this course — used for the attendance roster and to mark non-submitters Absent.</summary>
        Task<List<UniversityStudentDto>> GetEnrolledStudentsAsync(string universityCode, string courseCode);
    }

    /// <summary>Adapter-agnostic course shape — not tied to any EF entity, so a
    /// real API-backed university doesn't need a local Course table at all.</summary>
    public record UniversityCourseDto(
        string CourseCode,
        string CourseName,
        string? InstructorName,
        int Credits);

    /// <summary>Adapter-agnostic student profile shape.</summary>
    public record UniversityStudentDto(
        string StudentNumber,
        string FullName,
        string UniversityEmail,
        string? Major,
        int YearOfStudy);
}
