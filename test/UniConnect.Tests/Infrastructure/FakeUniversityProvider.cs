using UniConnect.Adapters;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// Stands in for a university's registrar API.
///
/// This is the highest-leverage seam in the codebase: every service that reads
/// academic data — enrollment checks, rosters, taught courses, student majors —
/// goes through IUniversityProvider and nothing else. Controlling this object
/// controls every academic precondition in the application.
///
/// Hand-written rather than mocked. The interface is eight methods of plain
/// data, and a test that reads "provider.Enroll(student, "CSC301")" says what it
/// means in a way that a chain of mock setups does not.
///
/// Set <see cref="FailWith"/> to make every call throw — that's how the
/// "adapter unavailable" edge cases (partial matching score, degraded course
/// data) get exercised.
/// </summary>
public sealed class FakeUniversityProvider : IUniversityProvider, IUniversityProviderResolver
{
    private readonly List<UniversityCourseDto> _courses = new();
    private readonly List<UniversityStudentDto> _students = new();
    private readonly List<UniversityInstructorDto> _instructors = new();
    private readonly List<UniversityStaffDto> _staff = new();

    // (studentNumber, courseCode)
    private readonly HashSet<(string Student, string Course)> _enrollments = new();
    // (instructorId, courseCode)
    private readonly HashSet<(string Instructor, string Course)> _teaching = new();

    /// <summary>When set, every method throws it — simulates an unreachable API.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>Counts calls so tests can assert the corpus/roster isn't re-fetched per item.</summary>
    public int CallCount { get; private set; }

    // ---------- arrangement ----------

    public FakeUniversityProvider WithCourse(
        string code, string name = "Course", string? instructorName = null, int credits = 3)
    {
        _courses.RemoveAll(c => c.CourseCode == code);
        _courses.Add(new UniversityCourseDto(code, name, instructorName, credits));
        return this;
    }

    public FakeUniversityProvider WithStudent(
        string studentNumber, string fullName = "Test Student", string? email = null,
        string? major = null, int year = 1)
    {
        _students.RemoveAll(s => s.StudentNumber == studentNumber);
        _students.Add(new UniversityStudentDto(
            studentNumber, fullName, email ?? $"{studentNumber.ToLowerInvariant()}@uni.edu", major, year));
        return this;
    }

    public FakeUniversityProvider WithInstructor(string staffId, string fullName = "Test Instructor")
    {
        _instructors.RemoveAll(i => i.StaffId == staffId);
        _instructors.Add(new UniversityInstructorDto(staffId, fullName, $"{staffId.ToLowerInvariant()}@uni.edu"));
        return this;
    }

    public FakeUniversityProvider WithStaff(string staffId, string department, string fullName = "Test Staff")
    {
        _staff.RemoveAll(s => s.StaffId == staffId);
        _staff.Add(new UniversityStaffDto(staffId, fullName, $"{staffId.ToLowerInvariant()}@uni.edu", department));
        return this;
    }

    public FakeUniversityProvider Enroll(string studentNumber, params string[] courseCodes)
    {
        foreach (var code in courseCodes) _enrollments.Add((studentNumber, code));
        return this;
    }

    public FakeUniversityProvider Unenroll(string studentNumber, string courseCode)
    {
        _enrollments.Remove((studentNumber, courseCode));
        return this;
    }

    public FakeUniversityProvider Teaches(string instructorId, params string[] courseCodes)
    {
        foreach (var code in courseCodes) _teaching.Add((instructorId, code));
        return this;
    }

    // ---------- IUniversityProvider ----------

    private void Guard()
    {
        CallCount++;
        if (FailWith is not null) throw FailWith;
    }

    public Task<bool> IsEnrolledAsync(string universityCode, string studentNumber, string courseCode)
    {
        Guard();
        return Task.FromResult(_enrollments.Contains((studentNumber, courseCode)));
    }

    public Task<List<UniversityCourseDto>> GetEnrolledCoursesAsync(string universityCode, string studentNumber)
    {
        Guard();
        var codes = _enrollments.Where(e => e.Student == studentNumber).Select(e => e.Course).ToHashSet();
        return Task.FromResult(_courses.Where(c => codes.Contains(c.CourseCode)).ToList());
    }

    public Task<List<UniversityCourseDto>> GetAllCoursesAsync(string universityCode)
    {
        Guard();
        return Task.FromResult(_courses.ToList());
    }

    public Task<UniversityStudentDto?> GetStudentInfoAsync(string universityCode, string studentNumber)
    {
        Guard();
        return Task.FromResult(_students.FirstOrDefault(s => s.StudentNumber == studentNumber));
    }

    public Task<UniversityInstructorDto?> GetInstructorInfoAsync(string universityCode, string staffId)
    {
        Guard();
        return Task.FromResult(_instructors.FirstOrDefault(i => i.StaffId == staffId));
    }

    public Task<UniversityStaffDto?> GetStaffInfoAsync(string universityCode, string staffId)
    {
        Guard();
        return Task.FromResult(_staff.FirstOrDefault(s => s.StaffId == staffId));
    }

    public Task<List<UniversityCourseDto>> GetTaughtCoursesAsync(string universityCode, string instructorId)
    {
        Guard();
        var codes = _teaching.Where(t => t.Instructor == instructorId).Select(t => t.Course).ToHashSet();
        return Task.FromResult(_courses.Where(c => codes.Contains(c.CourseCode)).ToList());
    }

    public Task<List<UniversityStudentDto>> GetEnrolledStudentsAsync(string universityCode, string courseCode)
    {
        Guard();
        var numbers = _enrollments.Where(e => e.Course == courseCode).Select(e => e.Student).ToHashSet();
        return Task.FromResult(_students.Where(s => numbers.Contains(s.StudentNumber)).ToList());
    }

    // ---------- IUniversityProviderResolver ----------
    // Implemented on the same object so a test can pass one instance wherever
    // either dependency is asked for.

    public Task<IUniversityProvider> GetProviderAsync(string universityCode)
        => Task.FromResult<IUniversityProvider>(this);
}
