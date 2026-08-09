using System.Net.Http.Json;
using UniConnect.Data;

namespace UniConnect.Adapters
{
    // ---------- DTOs matching the real "UMS Read-Only API v1" exactly ----------
    // Property names only need to match their JSON keys case-INSENSITIVELY —
    // System.Net.Http.Json's ReadFromJsonAsync defaults to case-insensitive
    // matching, so PascalCase here against their camelCase JSON is fine. What
    // must NOT be assumed is that the names are otherwise the same as our own
    // simulator's DTOs (ExternalStudentRecord etc.) — they aren't; see each
    // field's own comment below for the specific mismatch it's covering.

    file class UmsStudentDto
    {
        public string? StudentId { get; set; }          // NOT "StudentNumber"
        public string? FullName { get; set; }
        public string? UniversityEmail { get; set; }    // NOT "Email"
        public string? Major { get; set; }
        // Nullable — their own spec: "Derived value (OPEN #1); null until
        // admission/credit rule is confirmed." Confirmed genuinely null on
        // real data (see the Swagger screenshot from testing). A non-nullable
        // int here would throw on every real student right now.
        public int? YearOfStudy { get; set; }
    }

    file class UmsCourseSectionDto
    {
        public string? SectionName { get; set; }
        public List<string>? Instructors { get; set; }  // names only — no instructor ID anywhere in this API
    }

    file class UmsCourseCatalogDto
    {
        public string? CourseCode { get; set; }
        public string? CourseName { get; set; }
        public double? Credits { get; set; }             // NOT an int — e.g. "3.0"
        public List<UmsCourseSectionDto>? Sections { get; set; }
    }

    file class UmsEnrollmentDto
    {
        public string? CourseCode { get; set; }
        public string? CourseName { get; set; }
        public double? Credits { get; set; }
        public string? SectionName { get; set; }
        public List<string>? Instructors { get; set; }
    }

    file class UmsEnrollmentCheckDto
    {
        public bool Enrolled { get; set; }
    }

    file class UmsRosterEntryDto
    {
        public string? StudentId { get; set; }
        public string? FullName { get; set; }
        public string? SectionName { get; set; }
    }

    file class UmsInstructorCourseDto
    {
        public string? CourseCode { get; set; }
        public string? CourseName { get; set; }
        public double? Credits { get; set; }
        public string? SectionName { get; set; }
    }

    /// <summary>
    /// Talks to the real partner university's actual API (see the OpenAPI
    /// spec they sent — "UMS Read-Only API v1"). Selected by
    /// UniversityProviderResolver when University.ApiStyle == "Ums".
    ///
    /// KNOWN GAPS — deliberate, not oversights, each tied to something
    /// missing from their spec or still open on their end:
    ///   - GetInstructorInfoAsync / GetStaffInfoAsync always return null.
    ///     Their API has no instructor or staff directory endpoint at all —
    ///     instructor names only ever appear as free text inside a course
    ///     section, never with an ID. Instructor/department-staff
    ///     self-registration cannot work against this university until they
    ///     add one (flagged back to them separately from the two OPEN items).
    ///   - GetTaughtCoursesAsync calls their documented endpoint exactly as
    ///     specified, but nothing in their API tells us what a valid
    ///     instructorId actually looks like — worth asking them directly
    ///     before relying on this for real.
    ///   - A course's InstructorName only ever reflects the FIRST instructor
    ///     of the FIRST section — their model supports multiple
    ///     sections/instructors per course; ours (UniversityCourseDto)
    ///     doesn't. Fine for a single-section course, lossy otherwise.
    ///   - There is no local-cache fallback here the way
    ///     RealApiUniversityProvider has one — that fallback reads from
    ///     Students/Courses/Enrollments, which are only ever populated by
    ///     UniversityApiSyncRunner, and that job doesn't understand this
    ///     API's shape yet (a separate, bigger piece of work). Every call
    ///     here is genuinely live; if their API is down, these throw rather
    ///     than degrade gracefully.
    /// </summary>
    public class UmsApiUniversityProvider : IUniversityProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<UmsApiUniversityProvider> _logger;

        public UmsApiUniversityProvider(
            IHttpClientFactory httpClientFactory,
            ApplicationDbContext db,
            ILogger<UmsApiUniversityProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _db = db;
            _logger = logger;
        }

        // ApiBaseUrl for a "Ums"-style University row should be the bare
        // host, e.g. "http://85.112.66.69:809" — no "/v1" suffix. This
        // method adds "v1/" per call itself, and the one call that doesn't
        // live under /v1 (health) is handled separately in
        // TestConnectionAsync below with a leading "/" to escape back to root.
        private async Task<HttpClient> BuildClientAsync(string universityCode)
        {
            var university = await _db.Universities.FindAsync(universityCode)
                ?? throw new InvalidOperationException($"University '{universityCode}' not found.");

            if (string.IsNullOrWhiteSpace(university.ApiBaseUrl))
                throw new InvalidOperationException($"University '{universityCode}' has no ApiBaseUrl configured.");

            var client = _httpClientFactory.CreateClient("UniversityApi");
            client.BaseAddress = new Uri(university.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(20);
            if (!string.IsNullOrWhiteSpace(university.ApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", university.ApiKey);

            return client;
        }

        /// <summary>Liveness check against the real "/health" path (root, not under /v1).</summary>
        public async Task<bool> TestConnectionAsync(string universityCode)
        {
            var client = await BuildClientAsync(universityCode);
            var response = await client.GetAsync("/health"); // leading "/" — escapes back to root even though BaseAddress may include a path
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> IsEnrolledAsync(string universityCode, string studentNumber, string courseCode)
        {
            var client = await BuildClientAsync(universityCode);
            // Uses their dedicated check endpoint directly (endpoint 6) rather
            // than pulling the whole enrollment list and searching client-side.
            var response = await client.GetAsync($"v1/students/{studentNumber}/enrollments/{courseCode}");
            if (!response.IsSuccessStatusCode) return false;

            var result = await response.Content.ReadFromJsonAsync<UmsEnrollmentCheckDto>();
            return result?.Enrolled ?? false;
        }

        public async Task<List<UniversityCourseDto>> GetEnrolledCoursesAsync(string universityCode, string studentNumber)
        {
            var client = await BuildClientAsync(universityCode);
            var response = await client.GetAsync($"v1/students/{studentNumber}/enrollments");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"External API returned {response.StatusCode}");

            var enrollments = await response.Content.ReadFromJsonAsync<List<UmsEnrollmentDto>>() ?? new();
            return enrollments.Select(e => new UniversityCourseDto(
                e.CourseCode ?? string.Empty,
                e.CourseName ?? string.Empty,
                e.Instructors?.FirstOrDefault(),
                (int)Math.Round(e.Credits ?? 0))).ToList();
        }

        public async Task<List<UniversityCourseDto>> GetAllCoursesAsync(string universityCode)
        {
            var client = await BuildClientAsync(universityCode);
            var response = await client.GetAsync("v1/courses");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"External API returned {response.StatusCode}");

            var courses = await response.Content.ReadFromJsonAsync<List<UmsCourseCatalogDto>>() ?? new();
            return courses.Select(c => new UniversityCourseDto(
                c.CourseCode ?? string.Empty,
                c.CourseName ?? string.Empty,
                // Only the first section's first instructor — see class-level
                // comment on why this is lossy for multi-section courses.
                c.Sections?.FirstOrDefault()?.Instructors?.FirstOrDefault(),
                (int)Math.Round(c.Credits ?? 0))).ToList();
        }

        public async Task<UniversityStudentDto?> GetStudentInfoAsync(string universityCode, string studentNumber)
        {
            var client = await BuildClientAsync(universityCode);
            var response = await client.GetAsync($"v1/students/{studentNumber}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"External API returned {response.StatusCode}");

            var s = await response.Content.ReadFromJsonAsync<UmsStudentDto>();
            if (s is null) return null;

            return new UniversityStudentDto(
                s.StudentId ?? studentNumber,
                s.FullName ?? string.Empty,
                s.UniversityEmail ?? string.Empty,
                s.Major,
                // See UmsStudentDto.YearOfStudy — genuinely null right now on
                // their real data (their OPEN #1). UniversityStudentDto.YearOfStudy
                // isn't nullable, so this maps null -> 0 rather than guessing
                // a real value. Revisit once they confirm the admission/credit
                // rule and this stops coming back null.
                s.YearOfStudy ?? 0);
        }

        /// <summary>Always null — see class-level comment: this API has no instructor directory.</summary>
        public Task<UniversityInstructorDto?> GetInstructorInfoAsync(string universityCode, string staffId)
            => Task.FromResult<UniversityInstructorDto?>(null);

        /// <summary>Always null — see class-level comment: this API has no staff directory.</summary>
        public Task<UniversityStaffDto?> GetStaffInfoAsync(string universityCode, string staffId)
            => Task.FromResult<UniversityStaffDto?>(null);

        public async Task<List<UniversityCourseDto>> GetTaughtCoursesAsync(string universityCode, string instructorId)
        {
            var client = await BuildClientAsync(universityCode);
            var response = await client.GetAsync($"v1/instructors/{instructorId}/courses");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"External API returned {response.StatusCode}");

            var courses = await response.Content.ReadFromJsonAsync<List<UmsInstructorCourseDto>>() ?? new();
            return courses.Select(c => new UniversityCourseDto(
                c.CourseCode ?? string.Empty,
                c.CourseName ?? string.Empty,
                null, // this endpoint's own schema never repeats the instructor's own name back
                (int)Math.Round(c.Credits ?? 0))).ToList();
        }

        public async Task<List<UniversityStudentDto>> GetEnrolledStudentsAsync(string universityCode, string courseCode)
        {
            var client = await BuildClientAsync(universityCode);
            var response = await client.GetAsync($"v1/courses/{courseCode}/roster");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"External API returned {response.StatusCode}");

            var roster = await response.Content.ReadFromJsonAsync<List<UmsRosterEntryDto>>() ?? new();
            // Roster entries carry no email/major/year — this endpoint is
            // PII-gated specifically to studentId/fullName/sectionName only.
            // Fine for Attendance (which only needs to match a roster row to
            // an ApplicationUser by student ID), not a substitute for
            // GetStudentInfoAsync anywhere that actually needs those fields.
            return roster.Select(r => new UniversityStudentDto(
                r.StudentId ?? string.Empty,
                r.FullName ?? string.Empty,
                string.Empty,
                null,
                0)).ToList();
        }
    }
}
