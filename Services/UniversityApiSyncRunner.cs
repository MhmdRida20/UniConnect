using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.ExternalApi;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// The actual sync logic (test + pull + cache) for one university's
    /// external API — extracted into its own injectable service so both
    /// UniversityApiSyncService (the periodic background job) and the
    /// admin's manual "Sync Now" button call the exact same code path,
    /// rather than duplicating it.
    /// </summary>
    public class UniversityApiSyncRunner
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UniversityApiSyncRunner> _logger;

        public UniversityApiSyncRunner(
            ApplicationDbContext db,
            IHttpClientFactory httpClientFactory,
            ILogger<UniversityApiSyncRunner> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task SyncAllApiUniversitiesAsync(CancellationToken ct = default)
        {
            var apiUniversities = await _db.Universities
                .Where(u => u.IsActive)
                .ToListAsync(ct);

            foreach (var university in apiUniversities)
                await SyncOneUniversityAsync(university, ct);
        }

        public async Task SyncOneUniversityAsync(University university, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(university.ApiBaseUrl))
            {
                university.LastSyncStatus = "Failed";
                university.LastSyncError = "No ApiBaseUrl configured.";
                university.LastSyncAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return;
            }

            var client = _httpClientFactory.CreateClient("UniversityApi");
            client.BaseAddress = new Uri(university.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
            if (!string.IsNullOrWhiteSpace(university.ApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", university.ApiKey);

            try
            {
                // 1. TEST — a lightweight health check before pulling real data.
                var healthResponse = await client.GetAsync("health", ct);
                if (!healthResponse.IsSuccessStatusCode)
                    throw new HttpRequestException($"Health check failed: {healthResponse.StatusCode}");

                // 2. SYNC — every student first (so someone with zero
                // enrollments still shows up), then the course catalog and
                // each course's roster (which fills in enrollments).
                var studentsResponse = await client.GetAsync("students", ct);
                studentsResponse.EnsureSuccessStatusCode();
                var externalStudents = await studentsResponse.Content.ReadFromJsonAsync<List<ExternalStudentRecord>>(cancellationToken: ct) ?? new();

                var coursesResponse = await client.GetAsync("courses", ct);
                coursesResponse.EnsureSuccessStatusCode();
                var externalCourses = await coursesResponse.Content.ReadFromJsonAsync<List<ExternalCourseRecord>>(cancellationToken: ct) ?? new();

                var existingCourses = await _db.Courses.Where(c => c.UniversityCode == university.Code).ToListAsync(ct);
                var existingStudents = await _db.Students.Where(s => s.UniversityCode == university.Code).ToListAsync(ct);
                var existingEnrollments = await _db.Enrollments.Where(e => e.UniversityCode == university.Code).ToListAsync(ct);

                // Tracks every (student, course) pair the external side
                // actually reports right now — anything locally cached that
                // ISN'T in this set by the end means the external side
                // dropped it, and the local copy needs to be removed too.
                // Without this, a drop on the external side would sync
                // everything else correctly but leave the stale local
                // enrollment behind forever.
                var currentExternalEnrollments = new HashSet<(string StudentNumber, string CourseCode)>();

                foreach (var extStudent in externalStudents)
                {
                    var localStudent = existingStudents.FirstOrDefault(s => s.UniversityId == extStudent.StudentNumber);
                    if (localStudent is null)
                    {
                        localStudent = new Student { UniversityId = extStudent.StudentNumber, UniversityCode = university.Code };
                        _db.Students.Add(localStudent);
                        existingStudents.Add(localStudent);
                    }
                    localStudent.FullName = extStudent.FullName;
                    localStudent.UniversityEmail = extStudent.Email;
                    localStudent.Major = extStudent.Major;
                    localStudent.YearOfStudy = extStudent.YearOfStudy;
                }

                foreach (var ext in externalCourses)
                {
                    var local = existingCourses.FirstOrDefault(c => c.CourseCode == ext.CourseCode);
                    if (local is null)
                    {
                        local = new Course { UniversityCode = university.Code, CourseCode = ext.CourseCode };
                        _db.Courses.Add(local);
                        existingCourses.Add(local);
                    }
                    local.CourseName = ext.CourseName;
                    local.InstructorName = ext.InstructorName;
                    local.Credits = ext.Credits;

                    if (!string.IsNullOrWhiteSpace(ext.InstructorStaffId))
                    {
                        var matchedInstructor = await _db.Users.FirstOrDefaultAsync(
                            u => u.UniversityCode == university.Code && u.UniversityId == ext.InstructorStaffId, ct);
                        local.InstructorId = matchedInstructor?.Id;
                    }

                    var rosterResponse = await client.GetAsync($"courses/{ext.CourseCode}/roster", ct);
                    if (!rosterResponse.IsSuccessStatusCode) continue;
                    var roster = await rosterResponse.Content.ReadFromJsonAsync<List<ExternalStudentRecord>>(cancellationToken: ct) ?? new();

                    foreach (var extStudent in roster)
                    {
                        currentExternalEnrollments.Add((extStudent.StudentNumber, ext.CourseCode));

                        // The student row itself was already created/updated
                        // above from the full students list — this loop only
                        // needs to record the enrollment.
                        var localEnrollment = existingEnrollments.FirstOrDefault(
                            e => e.UniversityId == extStudent.StudentNumber && e.CourseCode == ext.CourseCode);
                        if (localEnrollment is null)
                        {
                            _db.Enrollments.Add(new Enrollment
                            {
                                UniversityCode = university.Code,
                                UniversityId = extStudent.StudentNumber,
                                CourseCode = ext.CourseCode,
                                Semester = "Synced"
                            });
                        }
                    }
                }

                // Remove any locally-cached enrollment for a course we DID
                // hear back from (i.e. its roster call succeeded) that the
                // external roster no longer lists — this is what makes a
                // drop actually take effect locally, not just get ignored.
                var coursesWeGotRostersFor = externalCourses.Select(c => c.CourseCode).ToHashSet();
                var staleEnrollments = existingEnrollments
                    .Where(e => coursesWeGotRostersFor.Contains(e.CourseCode)
                             && !currentExternalEnrollments.Contains((e.UniversityId, e.CourseCode)))
                    .ToList();
                if (staleEnrollments.Count > 0)
                    _db.Enrollments.RemoveRange(staleEnrollments);

                university.LastSyncAt = DateTime.UtcNow;
                university.LastSyncStatus = "Success";
                university.LastSyncError = null;
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Synced {Count} course(s) for university {Code}.", externalCourses.Count, university.Code);
            }
            catch (Exception ex)
            {
                university.LastSyncAt = DateTime.UtcNow;
                university.LastSyncStatus = "Failed";
                university.LastSyncError = ex.Message.Length > 290 ? ex.Message[..290] : ex.Message;
                await _db.SaveChangesAsync(ct);

                _logger.LogWarning(ex, "Sync failed for university {Code} — will retry next cycle.", university.Code);
            }
        }
    }
}
