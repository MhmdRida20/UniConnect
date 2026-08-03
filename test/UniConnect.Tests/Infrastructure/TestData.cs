using Microsoft.AspNetCore.Identity;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// Seeding helpers. Each one fills in the fields a test almost never cares
/// about and exposes only the ones it does, so a test body reads as the
/// scenario rather than as object construction.
/// </summary>
public static class TestData
{
    public const string DefaultUniversity = "DEFAULT";
    public const string OtherUniversity = "OTHER";

    public static University AddUniversity(this ApplicationDbContext db, string code = DefaultUniversity)
    {
        var university = new University
        {
            Code = code,
            Name = $"{code} University",
            ApiBaseUrl = $"https://localhost/external-api/v1",
            ApiKey = $"key-{code}",
            IsActive = true
        };
        db.Universities.Add(university);
        db.SaveChanges();
        return university;
    }

    /// <summary>
    /// A user row written directly, bypassing UserManager. Fine when the test
    /// is about something other than account creation; use UserManager when
    /// password hashing or validation is the point.
    /// </summary>
    public static ApplicationUser AddUser(
        this ApplicationDbContext db,
        string universityId,
        string universityCode = DefaultUniversity,
        string? fullName = null,
        string? department = null,
        bool suspended = false)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"{universityId.ToLowerInvariant()}@uni.edu",
            NormalizedUserName = $"{universityId.ToUpperInvariant()}@UNI.EDU",
            Email = $"{universityId.ToLowerInvariant()}@uni.edu",
            NormalizedEmail = $"{universityId.ToUpperInvariant()}@UNI.EDU",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            UniversityId = universityId,
            UniversityCode = universityCode,
            FullName = fullName ?? $"User {universityId}",
            Department = department,
            IsSuspended = suspended
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static void AddToRole(this ApplicationDbContext db, ApplicationUser user, string role)
    {
        var existing = db.Roles.FirstOrDefault(r => r.Name == role);
        if (existing is null)
        {
            existing = new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() };
            db.Roles.Add(existing);
            db.SaveChanges();
        }

        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = existing.Id });
        db.SaveChanges();
    }

    public static Course AddCourse(
        this ApplicationDbContext db,
        string courseCode,
        string universityCode = DefaultUniversity,
        string? name = null,
        string? instructorId = null,
        int credits = 3)
    {
        var course = new Course
        {
            UniversityCode = universityCode,
            CourseCode = courseCode,
            CourseName = name ?? $"Course {courseCode}",
            InstructorId = instructorId,
            Credits = credits
        };
        db.Courses.Add(course);
        db.SaveChanges();
        return course;
    }

    public static Student AddStudentRecord(
        this ApplicationDbContext db,
        string universityId,
        string universityCode = DefaultUniversity,
        string? major = null)
    {
        var student = new Student
        {
            UniversityId = universityId,
            UniversityCode = universityCode,
            FullName = $"Student {universityId}",
            UniversityEmail = $"{universityId.ToLowerInvariant()}@uni.edu",
            Major = major,
            YearOfStudy = 2
        };
        db.Students.Add(student);
        db.SaveChanges();
        return student;
    }

    public static Enrollment AddEnrollment(
        this ApplicationDbContext db,
        string universityId,
        string courseCode,
        string universityCode = DefaultUniversity)
    {
        var enrollment = new Enrollment
        {
            UniversityId = universityId,
            UniversityCode = universityCode,
            CourseCode = courseCode
        };
        db.Enrollments.Add(enrollment);
        db.SaveChanges();
        return enrollment;
    }

    /// <summary>
    /// An attendance session positioned relative to now, because the
    /// application reads the clock directly (see TEST_PLAN.md §2.2②).
    /// Negative <paramref name="startsInMinutes"/> means it already started.
    /// </summary>
    public static AttendanceSession AddSession(
        this ApplicationDbContext db,
        string instructorId,
        string courseCode = "CSC301",
        string universityCode = DefaultUniversity,
        string? token = null,
        double startsInMinutes = -5,
        double endsInMinutes = 55,
        double qrExpiresInMinutes = 55,
        int graceMinutes = 10,
        int radiusMeters = 100,
        double lat = 33.8938,
        double lng = 35.5018,
        AttendanceSessionStatus status = AttendanceSessionStatus.Active)
    {
        var now = DateTime.Now;
        var session = new AttendanceSession
        {
            UniversityCode = universityCode,
            CourseCode = courseCode,
            CourseName = $"Course {courseCode}",
            InstructorId = instructorId,
            SessionDate = now.Date,
            StartTime = now.AddMinutes(startsInMinutes),
            EndTime = now.AddMinutes(endsInMinutes),
            QrExpiresAt = now.AddMinutes(qrExpiresInMinutes),
            GracePeriodMinutes = graceMinutes,
            GpsRadiusMeters = radiusMeters,
            ClassroomLat = lat,
            ClassroomLng = lng,
            QrToken = token ?? Guid.NewGuid().ToString("N"),
            Status = status
        };
        db.AttendanceSessions.Add(session);
        db.SaveChanges();
        return session;
    }

    public static AttendanceRecord AddRecord(
        this ApplicationDbContext db,
        AttendanceSession session,
        ApplicationUser user,
        AttendanceStatus status,
        DateTime? submittedAt = null,
        bool suspicious = false)
    {
        var record = new AttendanceRecord
        {
            AttendanceSessionId = session.Id,
            UserId = user.Id,
            Status = status,
            SubmittedAt = submittedAt ?? (status == AttendanceStatus.Absent ? null : DateTime.Now),
            IsSuspicious = suspicious
        };
        db.AttendanceRecords.Add(record);
        db.SaveChanges();
        return record;
    }

    public static Company AddCompany(
        this ApplicationDbContext db,
        ApplicationUser owner,
        string universityCode = DefaultUniversity,
        string name = "Career Services")
    {
        var company = new Company
        {
            UserId = owner.Id,
            UniversityCode = universityCode,
            CompanyName = name,
            ContactEmail = "careers@uni.edu"
        };
        db.Companies.Add(company);
        db.SaveChanges();
        return company;
    }

    public static Internship AddInternship(
        this ApplicationDbContext db,
        Company company,
        string title = "Backend Intern",
        string? requiredSkills = null,
        string? recommendedCourses = null,
        string? relevantMajors = null,
        string location = "Beirut",
        string description = "Work on backend services.",
        bool active = true,
        InternshipPostingMode mode = InternshipPostingMode.FullApplication)
    {
        var internship = new Internship
        {
            CompanyId = company.Id,
            Title = title,
            Description = description,
            RequiredSkills = requiredSkills,
            RecommendedCourses = recommendedCourses,
            RelevantMajors = relevantMajors,
            Location = location,
            ApplicationDeadline = DateTime.UtcNow.AddDays(30),
            IsActive = active,
            PostingMode = mode,
            ExternalEmployerName = "DemoTech"
        };
        db.Internships.Add(internship);
        db.SaveChanges();
        return internship;
    }

    public static CareerProfile AddCareerProfile(
        this ApplicationDbContext db,
        ApplicationUser user,
        string? interests = null,
        string? preferredLocation = null)
    {
        var profile = new CareerProfile
        {
            UserId = user.Id,
            CareerInterests = interests,
            PreferredLocation = preferredLocation
        };
        db.CareerProfiles.Add(profile);
        db.SaveChanges();
        return profile;
    }

    public static void AddSkills(this ApplicationDbContext db, ApplicationUser user, params string[] skills)
    {
        foreach (var skill in skills)
            db.StudentSkills.Add(new StudentSkill { UserId = user.Id, SkillName = skill });
        db.SaveChanges();
    }

    public static void AddServiceCatalog(this ApplicationDbContext db, string universityCode = DefaultUniversity)
    {
        string[] codes =
        {
            ServiceCodes.StudyGroups, ServiceCodes.RideSharing, ServiceCodes.Attendance,
            ServiceCodes.Tickets, ServiceCodes.Internships, ServiceCodes.Clubs
        };

        foreach (var code in codes)
        {
            if (!db.Services.Any(s => s.Code == code))
                db.Services.Add(new Service { Code = code, Name = code, IsImplemented = true });
        }
        db.SaveChanges();

        foreach (var code in codes)
        {
            if (!db.UniversityServices.Any(us => us.UniversityCode == universityCode && us.ServiceCode == code))
                db.UniversityServices.Add(new UniversityService
                {
                    UniversityCode = universityCode,
                    ServiceCode = code,
                    IsEnabled = true
                });
        }
        db.SaveChanges();
    }
}
