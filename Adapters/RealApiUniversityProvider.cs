using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.ExternalApi;

namespace UniConnect.Adapters
{
    /// <summary>
    /// Genuinely calls a university's external API over HTTP (in this
    /// project, ExternalUniversityApiController — see that file's comments
    /// for why a real deployment wouldn't include it). Every call:
    ///   1. Sends the university's ApiKey as an X-Api-Key header.
    ///   2. Times out and fails safely rather than hanging the request.
    ///   3. Falls back to the last-known-good LOCAL cache (the same
    ///      Student/Course/Enrollment tables — the same local cache every
    ///      university's data lives in)
    ///      if the live call fails — Adapter Edge Cases: "Adapter
    ///      unavailable... the system shall use cached/fallback data."
    ///      That cache is kept fresh by UniversityApiSyncService, the
    ///      periodic background job — not by these on-demand calls, which
    ///      only fall back to whatever the last successful sync left behind.
    /// </summary>
    public class RealApiUniversityProvider : IUniversityProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<RealApiUniversityProvider> _logger;

        public RealApiUniversityProvider(
            IHttpClientFactory httpClientFactory,
            ApplicationDbContext db,
            ILogger<RealApiUniversityProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _db = db;
            _logger = logger;
        }

        private async Task<HttpClient> BuildClientAsync(string universityCode)
        {
            var university = await _db.Universities.FindAsync(universityCode)
                ?? throw new InvalidOperationException($"University '{universityCode}' not found.");

            if (string.IsNullOrWhiteSpace(university.ApiBaseUrl))
                throw new InvalidOperationException($"University '{universityCode}' has no ApiBaseUrl configured.");

            var client = _httpClientFactory.CreateClient("UniversityApi");
            client.BaseAddress = new Uri(university.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(8);
            if (!string.IsNullOrWhiteSpace(university.ApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", university.ApiKey);

            return client;
        }

        public async Task<bool> IsEnrolledAsync(string universityCode, string studentNumber, string courseCode)
        {
            try
            {
                var client = await BuildClientAsync(universityCode);
                var response = await client.GetAsync($"students/{studentNumber}/enrollments");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"External API returned {response.StatusCode}");

                var courses = await response.Content.ReadFromJsonAsync<List<ExternalCourseRecord>>() ?? new();
                return courses.Any(c => c.CourseCode == courseCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Live enrollment check failed for {University}/{Student}/{Course} — falling back to local cache.",
                    universityCode, studentNumber, courseCode);

                // Fall back to whatever the last successful sync cached locally.
                return await _db.Enrollments.AnyAsync(e =>
                    e.UniversityCode == universityCode && e.UniversityId == studentNumber && e.CourseCode == courseCode);
            }
        }

        public async Task<List<UniversityCourseDto>> GetEnrolledCoursesAsync(string universityCode, string studentNumber)
        {
            try
            {
                var client = await BuildClientAsync(universityCode);
                var response = await client.GetAsync($"students/{studentNumber}/enrollments");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"External API returned {response.StatusCode}");

                var courses = await response.Content.ReadFromJsonAsync<List<ExternalCourseRecord>>() ?? new();
                return courses.Select(c => new UniversityCourseDto(c.CourseCode, c.CourseName, c.InstructorName, c.Credits)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Live enrolled-courses lookup failed for {University}/{Student} — falling back to local cache.",
                    universityCode, studentNumber);

                return await _db.Enrollments
                    .Where(e => e.UniversityCode == universityCode && e.UniversityId == studentNumber)
                    .Include(e => e.Course)
                    .Where(e => e.Course != null)
                    .Select(e => new UniversityCourseDto(e.Course!.CourseCode, e.Course.CourseName, e.Course.InstructorName, e.Course.Credits))
                    .ToListAsync();
            }
        }

        public async Task<List<UniversityCourseDto>> GetAllCoursesAsync(string universityCode)
        {
            try
            {
                var client = await BuildClientAsync(universityCode);
                var response = await client.GetAsync("courses");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"External API returned {response.StatusCode}");

                var courses = await response.Content.ReadFromJsonAsync<List<ExternalCourseRecord>>() ?? new();
                return courses.Select(c => new UniversityCourseDto(c.CourseCode, c.CourseName, c.InstructorName, c.Credits)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live course-catalog lookup failed for {University} — falling back to local cache.", universityCode);

                return await _db.Courses
                    .Where(c => c.UniversityCode == universityCode)
                    .Select(c => new UniversityCourseDto(c.CourseCode, c.CourseName, c.InstructorName, c.Credits))
                    .ToListAsync();
            }
        }

        public async Task<UniversityStudentDto?> GetStudentInfoAsync(string universityCode, string studentNumber)
        {
            try
            {
                var client = await BuildClientAsync(universityCode);
                var response = await client.GetAsync($"students/{studentNumber}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"External API returned {response.StatusCode}");

                var s = await response.Content.ReadFromJsonAsync<ExternalStudentRecord>();
                return s is null ? null : new UniversityStudentDto(s.StudentNumber, s.FullName, s.Email, s.Major, s.YearOfStudy);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live student lookup failed for {University}/{Student} — falling back to local cache.", universityCode, studentNumber);

                var student = await _db.Students.FirstOrDefaultAsync(
                    s => s.UniversityCode == universityCode && s.UniversityId == studentNumber);
                return student is null ? null
                    : new UniversityStudentDto(student.UniversityId, student.FullName, student.UniversityEmail, student.Major, student.YearOfStudy);
            }
        }

        public async Task<List<UniversityCourseDto>> GetTaughtCoursesAsync(string universityCode, string instructorId)
        {
            try
            {
                var client = await BuildClientAsync(universityCode);
                var response = await client.GetAsync($"instructors/{instructorId}/courses");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"External API returned {response.StatusCode}");

                var courses = await response.Content.ReadFromJsonAsync<List<ExternalCourseRecord>>() ?? new();
                return courses.Select(c => new UniversityCourseDto(c.CourseCode, c.CourseName, c.InstructorName, c.Credits)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live taught-courses lookup failed for {University}/{Instructor} — falling back to local cache.", universityCode, instructorId);

                return await _db.Courses
                    .Where(c => c.UniversityCode == universityCode && c.InstructorId == instructorId)
                    .Select(c => new UniversityCourseDto(c.CourseCode, c.CourseName, c.InstructorName, c.Credits))
                    .ToListAsync();
            }
        }

        public async Task<List<UniversityStudentDto>> GetEnrolledStudentsAsync(string universityCode, string courseCode)
        {
            try
            {
                var client = await BuildClientAsync(universityCode);
                var response = await client.GetAsync($"courses/{courseCode}/roster");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"External API returned {response.StatusCode}");

                var students = await response.Content.ReadFromJsonAsync<List<ExternalStudentRecord>>() ?? new();
                return students.Select(s => new UniversityStudentDto(s.StudentNumber, s.FullName, s.Email, s.Major, s.YearOfStudy)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live roster lookup failed for {University}/{Course} — falling back to local cache.", universityCode, courseCode);

                return await _db.Enrollments
                    .Where(e => e.UniversityCode == universityCode && e.CourseCode == courseCode)
                    .Include(e => e.Student)
                    .Where(e => e.Student != null)
                    .Select(e => new UniversityStudentDto(e.Student!.UniversityId, e.Student.FullName, e.Student.UniversityEmail, e.Student.Major, e.Student.YearOfStudy))
                    .ToListAsync();
            }
        }
    }
}
