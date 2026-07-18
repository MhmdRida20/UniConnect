using Microsoft.EntityFrameworkCore;
using UniConnect.Data;

namespace UniConnect.Adapters
{
    /// <summary>
    /// Reads academic data from UniConnect's own seeded Student/Course/
    /// Enrollment tables. Used for the prototype, the demo, automated tests,
    /// and as the default for any university that hasn't connected a real
    /// registrar system yet.
    ///
    /// This is intentionally "dumb" — it doesn't know or care about business
    /// rules beyond "what does the data say". All of that behavior is
    /// identical to what StudyGroupsController used to do by querying
    /// _db.Enrollments directly; this class just centralizes it behind the
    /// adapter interface so it can be swapped per university.
    ///
    /// Every query here is scoped by universityCode — each university has its
    /// own Course catalog (composite key: UniversityCode + CourseCode), so
    /// two universities can both have a "CSC301" without colliding.
    /// </summary>
    public class MockUniversityProvider : IUniversityProvider
    {
        private readonly ApplicationDbContext _db;

        public MockUniversityProvider(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<bool> IsEnrolledAsync(string universityCode, string studentNumber, string courseCode)
        {
            return await _db.Enrollments.AnyAsync(
                e => e.UniversityId == studentNumber
                  && e.UniversityCode == universityCode
                  && e.CourseCode == courseCode);
        }

        public async Task<List<UniversityCourseDto>> GetEnrolledCoursesAsync(string universityCode, string studentNumber)
        {
            return await _db.Enrollments
                .Where(e => e.UniversityId == studentNumber && e.UniversityCode == universityCode)
                .Include(e => e.Course)
                .Where(e => e.Course != null)
                .Select(e => new UniversityCourseDto(
                    e.Course!.CourseCode,
                    e.Course.CourseName,
                    e.Course.InstructorName,
                    e.Course.Credits))
                .ToListAsync();
        }

        public async Task<List<UniversityCourseDto>> GetAllCoursesAsync(string universityCode)
        {
            return await _db.Courses
                .Where(c => c.UniversityCode == universityCode)
                .Select(c => new UniversityCourseDto(c.CourseCode, c.CourseName, c.InstructorName, c.Credits))
                .ToListAsync();
        }

        public async Task<UniversityStudentDto?> GetStudentInfoAsync(string universityCode, string studentNumber)
        {
            var student = await _db.Students.FirstOrDefaultAsync(
                s => s.UniversityId == studentNumber && s.UniversityCode == universityCode);
            if (student is null) return null;

            return new UniversityStudentDto(
                student.UniversityId,
                student.FullName,
                student.UniversityEmail,
                student.Major,
                student.YearOfStudy);
        }

        public async Task<List<UniversityCourseDto>> GetTaughtCoursesAsync(string universityCode, string instructorId)
        {
            return await _db.Courses
                .Where(c => c.UniversityCode == universityCode && c.InstructorId == instructorId)
                .Select(c => new UniversityCourseDto(c.CourseCode, c.CourseName, c.InstructorName, c.Credits))
                .ToListAsync();
        }

        public async Task<List<UniversityStudentDto>> GetEnrolledStudentsAsync(string universityCode, string courseCode)
        {
            return await _db.Enrollments
                .Where(e => e.UniversityCode == universityCode && e.CourseCode == courseCode)
                .Include(e => e.Student)
                .Where(e => e.Student != null)
                .Select(e => new UniversityStudentDto(
                    e.Student!.UniversityId,
                    e.Student.FullName,
                    e.Student.UniversityEmail,
                    e.Student.Major,
                    e.Student.YearOfStudy))
                .ToListAsync();
        }
    }
}
