using Microsoft.AspNetCore.Mvc;
using UniConnect.ExternalApi;

namespace UniConnect.Controllers.ExternalApi
{
    /// <summary>
    /// Stands in for a real university's registrar API. Every request is
    /// routed to a DIFFERENT dataset based on its X-Api-Key header — each
    /// university UniConnect creates gets its own independent key, and
    /// therefore its own independent students/courses, exactly like two
    /// real, separate university partners would never share a database.
    ///
    /// In a real deployment this controller wouldn't exist at all — it
    /// would be someone else's actual system.
    /// </summary>
    [ApiController]
    [Route("external-api/v1")]
    public class ExternalUniversityApiController : ControllerBase
    {
        private readonly ExternalUniversityDataStore _store;
        private readonly ILogger<ExternalUniversityApiController> _logger;
        private static readonly Random _random = new();

        public ExternalUniversityApiController(
            ExternalUniversityDataStore store,
            ILogger<ExternalUniversityApiController> logger)
        {
            _store = store;
            _logger = logger;
        }

        // Resolves which university's dataset this request is for, based on
        // its API key — the same two checks a real external partner's API
        // would apply: valid credentials, and (simulated) occasional
        // unavailability.
        private async Task<(ExternalUniversityDataset? dataset, IActionResult? denied)> ResolveAsync()
        {
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(providedKey))
            {
                _logger.LogWarning("External API rejected a request — missing X-Api-Key header.");
                return (null, Unauthorized(new { error = "Missing X-Api-Key header." }));
            }

            var dataset = await _store.GetDatasetAsync(providedKey);
            if (dataset is null)
            {
                _logger.LogWarning("External API rejected a request — no dataset provisioned for this API key.");
                return (null, Unauthorized(new { error = "Invalid API key." }));
            }

            if (_store.SimulatedFailureRatePercent > 0 && _random.Next(100) < _store.SimulatedFailureRatePercent)
            {
                _logger.LogWarning("External API simulated a failure for this request (demo instability).");
                return (null, StatusCode(503, new { error = "Service temporarily unavailable." }));
            }

            return (dataset, null);
        }

        [HttpGet("students/{studentNumber}")]
        public async Task<IActionResult> GetStudent(string studentNumber)
        {
            var (dataset, denied) = await ResolveAsync();
            if (denied is not null) return denied;

            var student = dataset!.Students.FirstOrDefault(s => s.StudentNumber == studentNumber);
            return student is null ? NotFound() : Ok(student);
        }

        [HttpGet("students/{studentNumber}/enrollments")]
        public async Task<IActionResult> GetStudentEnrollments(string studentNumber)
        {
            var (dataset, denied) = await ResolveAsync();
            if (denied is not null) return denied;

            var courseCodes = dataset!.Enrollments
                .Where(e => e.StudentNumber == studentNumber)
                .Select(e => e.CourseCode)
                .ToList();

            var courses = dataset.Courses.Where(c => courseCodes.Contains(c.CourseCode)).ToList();
            return Ok(courses);
        }

        [HttpGet("courses")]
        public async Task<IActionResult> GetAllCourses()
        {
            var (dataset, denied) = await ResolveAsync();
            if (denied is not null) return denied;

            return Ok(dataset!.Courses);
        }

        // Every student, regardless of whether they're enrolled in
        // anything yet — without this, a student with zero enrollments is
        // invisible to a sync that only discovers people through course
        // rosters.
        [HttpGet("students")]
        public async Task<IActionResult> GetAllStudents()
        {
            var (dataset, denied) = await ResolveAsync();
            if (denied is not null) return denied;

            return Ok(dataset!.Students);
        }

        [HttpGet("courses/{courseCode}/roster")]
        public async Task<IActionResult> GetRoster(string courseCode)
        {
            var (dataset, denied) = await ResolveAsync();
            if (denied is not null) return denied;

            var studentNumbers = dataset!.Enrollments
                .Where(e => e.CourseCode == courseCode)
                .Select(e => e.StudentNumber)
                .ToList();

            var roster = dataset.Students.Where(s => studentNumbers.Contains(s.StudentNumber)).ToList();
            return Ok(roster);
        }

        [HttpGet("instructors/{instructorStaffId}/courses")]
        public async Task<IActionResult> GetTaughtCourses(string instructorStaffId)
        {
            var (dataset, denied) = await ResolveAsync();
            if (denied is not null) return denied;

            var courses = dataset!.Courses.Where(c => c.InstructorStaffId == instructorStaffId).ToList();
            return Ok(courses);
        }

        [HttpGet("health")]
        public async Task<IActionResult> Health()
        {
            var (dataset, denied) = await ResolveAsync();
            if (denied is not null) return denied;

            return Ok(new { status = "ok", serverTimeUtc = DateTime.UtcNow, dataset = dataset!.Label });
        }
    }
}
