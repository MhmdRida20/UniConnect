using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.ExternalApi
{
    /// <summary>
    /// DTOs returned by the simulated external API — same shapes as before,
    /// just now backed by real persisted data (ExternalSimCourse/Student/
    /// Enrollment in Models/ExternalSimData.cs) instead of an in-memory
    /// dictionary that a restart would wipe.
    /// </summary>
    public class ExternalStudentRecord
    {
        public string StudentNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Major { get; set; }
        public int YearOfStudy { get; set; }
    }

    public class ExternalCourseRecord
    {
        public string CourseCode { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public string? InstructorName { get; set; }
        public string? InstructorStaffId { get; set; }
        public int Credits { get; set; }
    }

    public class ExternalEnrollmentRecord
    {
        public string StudentNumber { get; set; } = string.Empty;
        public string CourseCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// Distinguishes "actually enrolled them" from "they were already
    /// enrolled" — without this, re-enrolling an already-enrolled student
    /// silently reported success and would incorrectly notify them and
    /// re-sync as if something had changed, when nothing had.
    /// </summary>
    public enum AddEnrollmentResult { Added, AlreadyEnrolled, NotFound }

    /// <summary>
    /// One "external university's" complete, independent dataset — its own
    /// students, courses, and enrollments, isolated from every other
    /// dataset. Identified by the API key UniConnect was given for it,
    /// exactly like a real integration where each partner issues its own
    /// credentials.
    /// </summary>
    public class ExternalUniversityDataset
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty; // friendly name, shown in the admin simulator UI only
        public List<ExternalStudentRecord> Students { get; } = new();
        public List<ExternalCourseRecord> Courses { get; } = new();
        public List<ExternalEnrollmentRecord> Enrollments { get; } = new();
    }

    /// <summary>
    /// Reads/writes the simulated external university data — PERSISTED in
    /// the real database now (ExternalSimCourse/Student/Enrollment), not an
    /// in-memory dictionary. This means the exact same students/courses for
    /// a given university survive an app restart, rather than needing to be
    /// regenerated. Registered as Scoped (it needs a DbContext), not
    /// Singleton like the old in-memory version.
    /// </summary>
    public class ExternalUniversityDataStore
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;

        // Every new university needs at least this many students, each
        // enrolled in at least this many courses, so there's always
        // something substantial to sync and demo — not a token 1-2 rows.
        private const int MinCourses = 4;
        private const int MinStudents = 5;
        private const int MinEnrollmentsPerStudent = 4;

        private static readonly (string Code, string Name, string Instructor, int Credits)[] CoursePool =
        {
            ("EXT101", "Introduction to Economics", "Dr. Elias Nassar", 3),
            ("EXT205", "Organic Chemistry", "Dr. Farah Idriss", 4),
            ("EXT310", "Software Engineering", "Dr. Wael Haddad", 3),
            ("EXT150", "Principles of Marketing", "Dr. Rana Kfoury", 3),
            ("EXT220", "Cell Biology", "Dr. Omar Sleiman", 4),
            ("EXT330", "Database Systems", "Dr. Lea Boutros", 3),
            ("EXT180", "World History II", "Dr. Samer Abdallah", 3),
            ("EXT270", "Calculus III", "Dr. Nour Haidar", 4),
            ("EXT390", "Operating Systems", "Dr. Rita Younes", 3),
            ("EXT140", "Intro to Psychology", "Dr. Bassam Khalil", 3),
            ("EXT260", "Statistics", "Dr. Joumana Tannous", 3),
            ("EXT410", "Machine Learning", "Dr. Adnan Rammal", 3),
        };

        private static readonly (string First, string Last)[] NamePool =
        {
            ("Tarek","Fares"), ("Nadine","Saab"), ("Ziad","Rahal"), ("Yara","Chidiac"),
            ("Diana","Moukarzel"), ("Rami","Sfeir"), ("Layal","Ghosn"), ("Fadi","Zeidan"),
            ("Maya","Kanaan"), ("Elie","Tabet"), ("Sarah","Njeim"), ("Karim","Daou"),
        };

        private static readonly string[] Majors =
            { "Economics", "Chemistry", "Computer Science", "Marketing", "Biology", "History", "Mathematics", "Psychology" };

        public int SimulatedFailureRatePercent { get; }

        public ExternalUniversityDataStore(ApplicationDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
            SimulatedFailureRatePercent = config.GetValue<int?>("ExternalApiDemo:SimulatedFailureRatePercent") ?? 0;
        }

        public static string GenerateApiKey() => "ext-" + Guid.NewGuid().ToString("N")[..20];

        public async Task<ExternalUniversityDataset?> GetDatasetAsync(string apiKey)
        {
            var courses = await _db.ExternalSimCourses.Where(c => c.ApiKey == apiKey).ToListAsync();
            if (courses.Count == 0) return null; // no dataset provisioned for this key

            var students = await _db.ExternalSimStudents.Where(s => s.ApiKey == apiKey).ToListAsync();
            var enrollments = await _db.ExternalSimEnrollments.Where(e => e.ApiKey == apiKey).ToListAsync();
            var university = await _db.Universities.FirstOrDefaultAsync(u => u.ApiKey == apiKey);

            var ds = new ExternalUniversityDataset { ApiKey = apiKey, Label = university?.Name ?? apiKey };
            ds.Courses.AddRange(courses.Select(c => new ExternalCourseRecord
            {
                CourseCode = c.CourseCode,
                CourseName = c.CourseName,
                InstructorName = c.InstructorName,
                InstructorStaffId = c.InstructorStaffId,
                Credits = c.Credits
            }));
            ds.Students.AddRange(students.Select(s => new ExternalStudentRecord
            {
                StudentNumber = s.StudentNumber,
                FullName = s.FullName,
                Email = s.Email,
                Major = s.Major,
                YearOfStudy = s.YearOfStudy
            }));
            ds.Enrollments.AddRange(enrollments.Select(e => new ExternalEnrollmentRecord
            {
                StudentNumber = e.StudentNumber,
                CourseCode = e.CourseCode
            }));
            return ds;
        }

        // Called once at startup by DbSeeder to provision the default
        // university's dataset using the SAME data seeded locally (so the
        // very first sync doesn't change anything a tester already knows —
        // it's the same Ali Hassan / CSC301 / etc. data, just genuinely
        // served through the API now instead of being a static local-only
        // table). Safe to call repeatedly — does nothing if already provisioned.
        public async Task ProvisionKnownDatasetAsync(
            string apiKey,
            IEnumerable<ExternalStudentRecord> students,
            IEnumerable<ExternalCourseRecord> courses,
            IEnumerable<ExternalEnrollmentRecord> enrollments)
        {
            if (await _db.ExternalSimCourses.AnyAsync(c => c.ApiKey == apiKey)) return;

            _db.ExternalSimCourses.AddRange(courses.Select(c => new ExternalSimCourse
            {
                ApiKey = apiKey,
                CourseCode = c.CourseCode,
                CourseName = c.CourseName,
                InstructorName = c.InstructorName,
                InstructorStaffId = c.InstructorStaffId,
                Credits = c.Credits
            }));
            _db.ExternalSimStudents.AddRange(students.Select(s => new ExternalSimStudent
            {
                ApiKey = apiKey,
                StudentNumber = s.StudentNumber,
                FullName = s.FullName,
                Email = s.Email,
                Major = s.Major,
                YearOfStudy = s.YearOfStudy
            }));
            _db.ExternalSimEnrollments.AddRange(enrollments.Select(e => new ExternalSimEnrollment
            {
                ApiKey = apiKey,
                StudentNumber = e.StudentNumber,
                CourseCode = e.CourseCode
            }));
            await _db.SaveChangesAsync();
        }

        // Provisions a NEW, randomized, independent dataset for a brand new
        // university — called the moment an admin clicks "Generate API Key".
        // Guarantees at least MinStudents students, each enrolled in at
        // least MinEnrollmentsPerStudent courses. Persisted for real now, so
        // it's provisioned exactly ONCE per key, ever — an app restart
        // doesn't touch it.
        public async Task<(int StudentCount, int CourseCount)> ProvisionRandomDatasetAsync(string apiKey)
        {
            if (await _db.ExternalSimCourses.AnyAsync(c => c.ApiKey == apiKey))
            {
                var existingCount = await _db.ExternalSimStudents.CountAsync(s => s.ApiKey == apiKey);
                var existingCourseCount = await _db.ExternalSimCourses.CountAsync(c => c.ApiKey == apiKey);
                return (existingCount, existingCourseCount);
            }

            var rng = new Random();

            var courseCount = Math.Max(MinCourses, rng.Next(MinCourses, MinCourses + 3));
            var chosenCourses = CoursePool.OrderBy(_ => rng.Next()).Take(courseCount).ToList();
            var simCourses = chosenCourses.Select(c => new ExternalSimCourse
            {
                ApiKey = apiKey,
                CourseCode = c.Code,
                CourseName = c.Name,
                InstructorName = c.Instructor,
                Credits = c.Credits
            }).ToList();

            var studentCount = Math.Max(MinStudents, rng.Next(MinStudents, MinStudents + 3));
            var chosenNames = NamePool.OrderBy(_ => rng.Next()).Take(studentCount).ToList();
            while (chosenNames.Count < studentCount)
                chosenNames.Add(NamePool[chosenNames.Count % NamePool.Length]);

            var simStudents = new List<ExternalSimStudent>();
            var studentNumbers = new List<string>();
            for (int i = 0; i < chosenNames.Count; i++)
            {
                var (first, last) = chosenNames[i];
                var number = $"EXT-{rng.Next(4000, 9999)}-{i}";
                studentNumbers.Add(number);
                simStudents.Add(new ExternalSimStudent
                {
                    ApiKey = apiKey,
                    StudentNumber = number,
                    FullName = $"{first} {last}",
                    Email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{i}@external-uni.edu",
                    Major = Majors[rng.Next(Majors.Length)],
                    YearOfStudy = rng.Next(1, 5)
                });
            }

            var simEnrollments = new List<ExternalSimEnrollment>();
            var enrollPerStudent = Math.Min(courseCount, Math.Max(MinEnrollmentsPerStudent, rng.Next(MinEnrollmentsPerStudent, courseCount + 1)));
            foreach (var studentNumber in studentNumbers)
            {
                foreach (var course in simCourses.OrderBy(_ => rng.Next()).Take(enrollPerStudent))
                    simEnrollments.Add(new ExternalSimEnrollment { ApiKey = apiKey, StudentNumber = studentNumber, CourseCode = course.CourseCode });
            }

            _db.ExternalSimCourses.AddRange(simCourses);
            _db.ExternalSimStudents.AddRange(simStudents);
            _db.ExternalSimEnrollments.AddRange(simEnrollments);
            await _db.SaveChangesAsync();

            return (simStudents.Count, simCourses.Count);
        }

        // ---------- Test/demo mutation methods (per-dataset) --------------------
        public async Task<string?> AddStudentAsync(string apiKey, string fullName, string email, string? major, int yearOfStudy)
        {
            if (!await _db.ExternalSimCourses.AnyAsync(c => c.ApiKey == apiKey)) return null;

            var existingCount = await _db.ExternalSimStudents.CountAsync(s => s.ApiKey == apiKey);
            var number = $"EXT-{3000 + existingCount}-{Guid.NewGuid().ToString("N")[..4]}";

            _db.ExternalSimStudents.Add(new ExternalSimStudent
            {
                ApiKey = apiKey,
                StudentNumber = number,
                FullName = fullName,
                Email = email,
                Major = major,
                YearOfStudy = yearOfStudy
            });
            await _db.SaveChangesAsync();
            return number;
        }

        public async Task<AddEnrollmentResult> AddEnrollmentAsync(string apiKey, string studentNumber, string courseCode)
        {
            var studentExists = await _db.ExternalSimStudents.AnyAsync(s => s.ApiKey == apiKey && s.StudentNumber == studentNumber);
            var courseExists = await _db.ExternalSimCourses.AnyAsync(c => c.ApiKey == apiKey && c.CourseCode == courseCode);
            if (!studentExists || !courseExists) return AddEnrollmentResult.NotFound;

            var already = await _db.ExternalSimEnrollments.AnyAsync(
                e => e.ApiKey == apiKey && e.StudentNumber == studentNumber && e.CourseCode == courseCode);
            if (already) return AddEnrollmentResult.AlreadyEnrolled;

            _db.ExternalSimEnrollments.Add(new ExternalSimEnrollment { ApiKey = apiKey, StudentNumber = studentNumber, CourseCode = courseCode });
            await _db.SaveChangesAsync();
            return AddEnrollmentResult.Added;
        }

        public async Task<bool> RemoveEnrollmentAsync(string apiKey, string studentNumber, string courseCode)
        {
            var existing = await _db.ExternalSimEnrollments.FirstOrDefaultAsync(
                e => e.ApiKey == apiKey && e.StudentNumber == studentNumber && e.CourseCode == courseCode);
            if (existing is null) return false;

            _db.ExternalSimEnrollments.Remove(existing);
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
