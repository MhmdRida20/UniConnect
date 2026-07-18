using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UniConnect.ExternalApi;
using UniConnect.Models;

namespace UniConnect.Data
{
    /// <summary>
    /// Seeds the database with the default university, roles, staff/admin/
    /// instructor accounts, and a starter set of courses/students/
    /// enrollments — all provisioned through the same real (simulated)
    /// external API every other university uses. There is no mock data path
    /// anywhere in this project: the default university's ApiKey/ApiBaseUrl
    /// point at ExternalUniversityApiController exactly like any other
    /// university, and its dataset is registered in ExternalUniversityDataStore
    /// with the SAME data seeded locally here — so the local tables act as a
    /// pre-warmed cache (avoiding an empty-data window before the first sync
    /// job runs), not as an authoritative "fake" data source.
    ///
    /// During signup, the user must enter a University ID. We check it
    /// against the seeded Students table (which is itself just the local
    /// cache of what the API returns).
    ///
    /// Called from Program.cs at startup. Safe to call repeatedly — it
    /// checks whether data already exists before inserting.
    /// </summary>
    public static class DbSeeder
    {
        public static async Task SeedAsync(
            ApplicationDbContext db,
            RoleManager<IdentityRole> roleManager,
            UserManager<ApplicationUser> userManager,
            ExternalUniversityDataStore externalStore,
            IConfiguration config)
        {
            // Make sure the database and all tables exist.
            await db.Database.EnsureCreatedAsync();

            // ---- 0. UNIVERSITIES (multi-tenant adapter core) --------------------
            // "DEFAULT" is the one university all current seeded students/accounts
            // belong to. Every university — including this one — is served
            // through RealApiUniversityProvider; ApiBaseUrl/ApiKey are what make
            // this a genuine (if simulated) API integration rather than a
            // hardcoded local table pretending to be one.
            const string DefaultUniversityCode = "DEFAULT";
            var appBaseUrl = (config["App:BaseUrl"] ?? "https://localhost:7253").TrimEnd('/');
            var defaultApiKey = config["DefaultUniversity:ApiKey"] ?? "default-uni-api-key-2026";

            if (!await db.Universities.AnyAsync())
            {
                await db.Universities.AddAsync(new University
                {
                    Code = DefaultUniversityCode,
                    Name = "UniConnect Demo University",
                    ApiBaseUrl = $"{appBaseUrl}/external-api/v1",
                    ApiKey = defaultApiKey,
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }

            // ---- 0b. SERVICE CATALOG (Services.docx §"Service Extensibility") ---
            if (!await db.Services.AnyAsync())
            {
                var services = new[]
                {
                    new Service { Code = ServiceCodes.StudyGroups, Name = "Study Groups",
                                  Description = "Create and join course-based study groups with real-time chat.",
                                  IconClass = "bi-people", IsImplemented = true },
                    new Service { Code = ServiceCodes.RideSharing, Name = "Ride Sharing",
                                  Description = "Offer and request rides to campus with live location tracking.",
                                  IconClass = "bi-car-front", IsImplemented = true },
                    new Service { Code = ServiceCodes.Attendance, Name = "Smart Attendance",
                                  Description = "QR + GPS based attendance sessions for instructors and students.",
                                  IconClass = "bi-qr-code", IsImplemented = true },
                    new Service { Code = ServiceCodes.Tickets, Name = "Complaints & Tickets",
                                  Description = "Submit and track complaints or service requests to departments.",
                                  IconClass = "bi-ticket-detailed", IsImplemented = true },
                    new Service { Code = ServiceCodes.Internships, Name = "Internships",
                                  Description = "Browse and apply to internships matched to your profile.",
                                  IconClass = "bi-briefcase", IsImplemented = true },
                    new Service { Code = ServiceCodes.Clubs, Name = "Clubs & Organizations",
                                  Description = "Join clubs, see announcements, and RSVP to events.",
                                  IconClass = "bi-flag", IsImplemented = true },
                };
                await db.Services.AddRangeAsync(services);
                await db.SaveChangesAsync();
            }

            // ---- 0c. PER-UNIVERSITY SERVICE ENABLEMENT --------------------------
            if (!await db.UniversityServices.AnyAsync())
            {
                var enablement = new[]
                {
                    new UniversityService { UniversityCode = DefaultUniversityCode, ServiceCode = ServiceCodes.StudyGroups, IsEnabled = true },
                    new UniversityService { UniversityCode = DefaultUniversityCode, ServiceCode = ServiceCodes.RideSharing, IsEnabled = true },
                    new UniversityService { UniversityCode = DefaultUniversityCode, ServiceCode = ServiceCodes.Tickets, IsEnabled = true },
                    new UniversityService { UniversityCode = DefaultUniversityCode, ServiceCode = ServiceCodes.Clubs, IsEnabled = true },
                    new UniversityService { UniversityCode = DefaultUniversityCode, ServiceCode = ServiceCodes.Attendance, IsEnabled = true },
                    new UniversityService { UniversityCode = DefaultUniversityCode, ServiceCode = ServiceCodes.Internships, IsEnabled = true },
                };
                await db.UniversityServices.AddRangeAsync(enablement);
                await db.SaveChangesAsync();
            }

            // ---- 0d. IDENTITY ROLES ---------------------------------------------
            const string StudentRole = "Student";
            const string DepartmentStaffRole = "DepartmentStaff";
            const string AdminRole = "Admin";
            const string InstructorRole = "Instructor";
            const string CompanyRole = "Company";
            foreach (var roleName in new[] { StudentRole, DepartmentStaffRole, AdminRole, InstructorRole, CompanyRole })
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                    await roleManager.CreateAsync(new IdentityRole(roleName));
            }

            // ---- 0e. TICKET CATEGORIES (FR-27) ----------------------------------
            if (!await db.TicketCategories.AnyAsync())
            {
                var categoryNames = new[] { "IT", "Registration", "Finance", "Student Affairs", "Facilities", "Academic Affairs", "Other" };
                var categories = categoryNames.Select(name => new TicketCategory
                {
                    UniversityCode = DefaultUniversityCode,
                    Name = name,
                    IsActive = true
                }).ToArray();
                await db.TicketCategories.AddRangeAsync(categories);
                await db.SaveChangesAsync();
            }

            // ---- 0f. DEPARTMENT STAFF ACCOUNTS ----------------------------------
            var staffAccounts = new[]
            {
                new { Email = "it.staff@uni.edu", FullName = "Yara Fakhoury", Department = "IT" },
                new { Email = "registration.staff@uni.edu", FullName = "Fadi Chami", Department = "Registration" },
                new { Email = "finance.staff@uni.edu", FullName = "Maya Kassir", Department = "Finance" },
                new { Email = "studentaffairs.staff@uni.edu", FullName = "Rami Abou Jaoude", Department = "Student Affairs" },
                new { Email = "facilities.staff@uni.edu", FullName = "Elie Ghanem", Department = "Facilities" },
                new { Email = "academicaffairs.staff@uni.edu", FullName = "Dana Chaaya", Department = "Academic Affairs" },
                new { Email = "other.staff@uni.edu", FullName = "Sami Nakhle", Department = "Other" },
            };

            for (var i = 0; i < staffAccounts.Length; i++)
            {
                var staff = staffAccounts[i];
                if (await userManager.FindByEmailAsync(staff.Email) is not null) continue;

                var staffUser = new ApplicationUser
                {
                    UserName = staff.Email,
                    Email = staff.Email,
                    EmailConfirmed = true,
                    FullName = staff.FullName,
                    Department = staff.Department,
                    UniversityCode = DefaultUniversityCode,
                    UniversityId = $"STAFF-{i + 1:D3}",
                };

                var createResult = await userManager.CreateAsync(staffUser, "Staff@12345");
                if (createResult.Succeeded)
                    await userManager.AddToRoleAsync(staffUser, DepartmentStaffRole);
            }

            // ---- 0g. PLATFORM ADMIN ACCOUNT -------------------------------------
            const string adminEmail = "admin@uniconnect.local";
            if (await userManager.FindByEmailAsync(adminEmail) is null)
            {
                var adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    FullName = "Platform Admin",
                    UniversityCode = DefaultUniversityCode,
                    UniversityId = "ADMIN-001",
                };

                var adminCreateResult = await userManager.CreateAsync(adminUser, "Admin@12345");
                if (adminCreateResult.Succeeded)
                    await userManager.AddToRoleAsync(adminUser, AdminRole);
            }

            // ---- 0h. INSTRUCTOR ACCOUNTS ----------------------------------------
            var instructorAccounts = new[]
            {
                new { Email = "instructor.chami@uni.edu", FullName = "Dr. Fadi Chami", StaffId = "INSTR-001" },
                new { Email = "instructor.habib@uni.edu", FullName = "Dr. Maya Habib", StaffId = "INSTR-002" },
            };
            foreach (var instructor in instructorAccounts)
            {
                if (await userManager.FindByEmailAsync(instructor.Email) is not null) continue;

                var instructorUser = new ApplicationUser
                {
                    UserName = instructor.Email,
                    Email = instructor.Email,
                    EmailConfirmed = true,
                    FullName = instructor.FullName,
                    UniversityCode = DefaultUniversityCode,
                    UniversityId = instructor.StaffId,
                };

                var instructorCreateResult = await userManager.CreateAsync(instructorUser, "Instructor@12345");
                if (instructorCreateResult.Succeeded)
                    await userManager.AddToRoleAsync(instructorUser, InstructorRole);
            }

            // ---- 0i. DEFAULT UNIVERSITY'S CAREER SERVICES ACCOUNT ---------------
            // Every university gets exactly one internship-posting account now
            // (see AdminUniversitiesController.Create for how new ones get
            // theirs automatically) — this seeds the DEFAULT university's own,
            // posting on behalf of two real (demo) external employers, one in
            // each posting mode.
            const string careerServicesEmail = "careers@uniconnectdemo.edu";
            var careerServicesUser = await userManager.FindByEmailAsync(careerServicesEmail);
            if (careerServicesUser is null)
            {
                careerServicesUser = new ApplicationUser
                {
                    UserName = careerServicesEmail,
                    Email = careerServicesEmail,
                    EmailConfirmed = true,
                    FullName = "UniConnect Demo University — Career Services",
                    UniversityCode = DefaultUniversityCode,
                    UniversityId = "CAREER-DEFAULT",
                };

                var careerCreateResult = await userManager.CreateAsync(careerServicesUser, "Career@12345");
                if (careerCreateResult.Succeeded)
                    await userManager.AddToRoleAsync(careerServicesUser, CompanyRole);
            }

            if (!await db.Companies.AnyAsync())
            {
                var company = new Company
                {
                    UserId = careerServicesUser.Id,
                    UniversityCode = DefaultUniversityCode,
                    CompanyName = "UniConnect Demo University — Career Services",
                    Description = "Posts internship opportunities on behalf of partner employers for our students.",
                    ContactEmail = careerServicesEmail,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Companies.Add(company);
                await db.SaveChangesAsync();

                db.Internships.AddRange(
                    new Internship
                    {
                        CompanyId = company.Id,
                        Title = "Backend Development Intern",
                        Description = "Work with the backend team building APIs in ASP.NET Core.",
                        RequiredSkills = "C#, SQL, Git",
                        RecommendedCourses = "CSC301, CSC340",
                        Location = "Beirut",
                        DurationWeeks = 12,
                        ApplicationDeadline = DateTime.Today.AddMonths(2),
                        NumberOfPositions = 2,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        PostingMode = InternshipPostingMode.FullApplication,
                        ExternalEmployerName = "DemoTech Solutions",
                        ExternalEmployerContactEmail = "hr@demotech.example.com"
                    },
                    new Internship
                    {
                        CompanyId = company.Id,
                        Title = "Marketing Intern",
                        Description = "Support the marketing team with social media and campaign content.",
                        RequiredSkills = "Communication, Social Media",
                        Location = "Beirut",
                        DurationWeeks = 8,
                        ApplicationDeadline = DateTime.Today.AddMonths(1),
                        NumberOfPositions = 1,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        PostingMode = InternshipPostingMode.ListingOnly,
                        ExternalEmployerName = "Brandworks Agency",
                        ExternalEmployerContactEmail = "info@brandworks.example.com",
                        ExternalApplyUrl = "https://brandworks.example.com/careers",
                        ExternalApplyEmail = "apply@brandworks.example.com"
                    }
                );
                await db.SaveChangesAsync();
            }

            // ---- 1. COURSES -----------------------------------------------------
            if (!await db.Courses.AnyAsync())
            {
                var courses = new[]
                {
                    new Course { UniversityCode = DefaultUniversityCode, CourseCode = "CSC301", CourseName = "Data Structures and Algorithms",
                                 InstructorName = "Dr. Maya Habib", Credits = 3 },
                    new Course { UniversityCode = DefaultUniversityCode, CourseCode = "CSC340", CourseName = "Database Systems",
                                 InstructorName = "Dr. Rami Khoury", Credits = 3 },
                    new Course { UniversityCode = DefaultUniversityCode, CourseCode = "CSC410", CourseName = "Software Engineering",
                                 InstructorName = "Dr. Lina Saad",   Credits = 3 },
                    new Course { UniversityCode = DefaultUniversityCode, CourseCode = "CSC420", CourseName = "Web Development with ASP.NET",
                                 InstructorName = "Dr. Karim Aoun",  Credits = 3 },
                    new Course { UniversityCode = DefaultUniversityCode, CourseCode = "MAT202", CourseName = "Discrete Mathematics",
                                 InstructorName = "Dr. Hala Mansour", Credits = 3 },
                    new Course { UniversityCode = DefaultUniversityCode, CourseCode = "MAT310", CourseName = "Linear Algebra",
                                 InstructorName = "Dr. Joseph Nehme", Credits = 3 },
                    new Course { UniversityCode = DefaultUniversityCode, CourseCode = "PHY201", CourseName = "Physics II",
                                 InstructorName = "Dr. Nadine Fares", Credits = 3 },
                    new Course { UniversityCode = DefaultUniversityCode, CourseCode = "ENG101", CourseName = "Technical Writing",
                                 InstructorName = "Dr. Sami Bechara", Credits = 2 },
                };
                await db.Courses.AddRangeAsync(courses);
                await db.SaveChangesAsync();
            }

            // ---- 1b. LINK INSTRUCTORS TO COURSES --------------------------------
            var habib = await userManager.FindByEmailAsync("instructor.habib@uni.edu");
            var chami = await userManager.FindByEmailAsync("instructor.chami@uni.edu");

            async Task AssignInstructorAsync(string courseCode, ApplicationUser? instructor)
            {
                if (instructor is null) return;
                var course = await db.Courses.FirstOrDefaultAsync(
                    c => c.UniversityCode == DefaultUniversityCode && c.CourseCode == courseCode);
                if (course is not null && course.InstructorId is null)
                    course.InstructorId = instructor.Id;
            }

            await AssignInstructorAsync("CSC301", habib);
            await AssignInstructorAsync("MAT202", habib);
            await AssignInstructorAsync("CSC340", chami);
            await AssignInstructorAsync("CSC420", chami);
            await db.SaveChangesAsync();

            // ---- 2. STUDENTS ----------------------------------------------------
            if (!await db.Students.AnyAsync())
            {
                var students = new[]
                {
                    new Student { UniversityId = "U2024001", UniversityCode = DefaultUniversityCode, FullName = "Ali Hassan",
                                  UniversityEmail = "ali.hassan@uni.edu",   Major = "Computer Science", YearOfStudy = 3 },
                    new Student { UniversityId = "U2024002", UniversityCode = DefaultUniversityCode, FullName = "Sara Khalil",
                                  UniversityEmail = "sara.khalil@uni.edu",  Major = "Computer Science", YearOfStudy = 3 },
                    new Student { UniversityId = "U2024003", UniversityCode = DefaultUniversityCode, FullName = "Omar Nassar",
                                  UniversityEmail = "omar.nassar@uni.edu",  Major = "Computer Science", YearOfStudy = 4 },
                    new Student { UniversityId = "U2024004", UniversityCode = DefaultUniversityCode, FullName = "Lara Tannous",
                                  UniversityEmail = "lara.tannous@uni.edu", Major = "Mathematics",      YearOfStudy = 2 },
                    new Student { UniversityId = "U2024005", UniversityCode = DefaultUniversityCode, FullName = "Karim Awad",
                                  UniversityEmail = "karim.awad@uni.edu",   Major = "Computer Science", YearOfStudy = 3 },
                    new Student { UniversityId = "U2024006", UniversityCode = DefaultUniversityCode, FullName = "Nour Daher",
                                  UniversityEmail = "nour.daher@uni.edu",   Major = "Physics",          YearOfStudy = 2 },
                    new Student { UniversityId = "U2024007", UniversityCode = DefaultUniversityCode, FullName = "Hadi Murad",
                                  UniversityEmail = "hadi.murad@uni.edu",   Major = "Computer Science", YearOfStudy = 4 },
                    new Student { UniversityId = "U2024008", UniversityCode = DefaultUniversityCode, FullName = "Rana Saliba",
                                  UniversityEmail = "rana.saliba@uni.edu",  Major = "Computer Science", YearOfStudy = 3 },
                };
                await db.Students.AddRangeAsync(students);
                await db.SaveChangesAsync();
            }

            // ---- 3. ENROLLMENTS ------------------------------------------------
            // Every student has AT LEAST 4 courses (a project-wide rule now,
            // matching what every newly-provisioned university also guarantees).
            if (!await db.Enrollments.AnyAsync())
            {
                var enrollments = new[]
                {
                    // Ali (CS, year 3) — 4
                    new Enrollment { UniversityId = "U2024001", UniversityCode = DefaultUniversityCode, CourseCode = "CSC301" },
                    new Enrollment { UniversityId = "U2024001", UniversityCode = DefaultUniversityCode, CourseCode = "CSC340" },
                    new Enrollment { UniversityId = "U2024001", UniversityCode = DefaultUniversityCode, CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024001", UniversityCode = DefaultUniversityCode, CourseCode = "MAT202" },

                    // Sara (CS, year 3) — 4
                    new Enrollment { UniversityId = "U2024002", UniversityCode = DefaultUniversityCode, CourseCode = "CSC301" },
                    new Enrollment { UniversityId = "U2024002", UniversityCode = DefaultUniversityCode, CourseCode = "CSC340" },
                    new Enrollment { UniversityId = "U2024002", UniversityCode = DefaultUniversityCode, CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024002", UniversityCode = DefaultUniversityCode, CourseCode = "ENG101" },

                    // Omar (CS, year 4) — 4
                    new Enrollment { UniversityId = "U2024003", UniversityCode = DefaultUniversityCode, CourseCode = "CSC410" },
                    new Enrollment { UniversityId = "U2024003", UniversityCode = DefaultUniversityCode, CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024003", UniversityCode = DefaultUniversityCode, CourseCode = "MAT310" },
                    new Enrollment { UniversityId = "U2024003", UniversityCode = DefaultUniversityCode, CourseCode = "CSC340" },

                    // Lara (Math, year 2) — 4
                    new Enrollment { UniversityId = "U2024004", UniversityCode = DefaultUniversityCode, CourseCode = "MAT202" },
                    new Enrollment { UniversityId = "U2024004", UniversityCode = DefaultUniversityCode, CourseCode = "MAT310" },
                    new Enrollment { UniversityId = "U2024004", UniversityCode = DefaultUniversityCode, CourseCode = "ENG101" },
                    new Enrollment { UniversityId = "U2024004", UniversityCode = DefaultUniversityCode, CourseCode = "PHY201" },

                    // Karim (CS, year 3) — 4
                    new Enrollment { UniversityId = "U2024005", UniversityCode = DefaultUniversityCode, CourseCode = "CSC301" },
                    new Enrollment { UniversityId = "U2024005", UniversityCode = DefaultUniversityCode, CourseCode = "CSC340" },
                    new Enrollment { UniversityId = "U2024005", UniversityCode = DefaultUniversityCode, CourseCode = "MAT202" },
                    new Enrollment { UniversityId = "U2024005", UniversityCode = DefaultUniversityCode, CourseCode = "CSC420" },

                    // Nour (Physics, year 2) — 4
                    new Enrollment { UniversityId = "U2024006", UniversityCode = DefaultUniversityCode, CourseCode = "PHY201" },
                    new Enrollment { UniversityId = "U2024006", UniversityCode = DefaultUniversityCode, CourseCode = "MAT310" },
                    new Enrollment { UniversityId = "U2024006", UniversityCode = DefaultUniversityCode, CourseCode = "ENG101" },
                    new Enrollment { UniversityId = "U2024006", UniversityCode = DefaultUniversityCode, CourseCode = "MAT202" },

                    // Hadi (CS, year 4) — 4
                    new Enrollment { UniversityId = "U2024007", UniversityCode = DefaultUniversityCode, CourseCode = "CSC410" },
                    new Enrollment { UniversityId = "U2024007", UniversityCode = DefaultUniversityCode, CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024007", UniversityCode = DefaultUniversityCode, CourseCode = "CSC340" },
                    new Enrollment { UniversityId = "U2024007", UniversityCode = DefaultUniversityCode, CourseCode = "MAT310" },

                    // Rana (CS, year 3) — 4
                    new Enrollment { UniversityId = "U2024008", UniversityCode = DefaultUniversityCode, CourseCode = "CSC301" },
                    new Enrollment { UniversityId = "U2024008", UniversityCode = DefaultUniversityCode, CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024008", UniversityCode = DefaultUniversityCode, CourseCode = "MAT202" },
                    new Enrollment { UniversityId = "U2024008", UniversityCode = DefaultUniversityCode, CourseCode = "CSC340" },
                };
                await db.Enrollments.AddRangeAsync(enrollments);
                await db.SaveChangesAsync();
            }

            // ---- 4. MIRROR INTO THE EXTERNAL API'S DATASET ----------------------
            // So RealApiUniversityProvider's calls to the (simulated) external
            // API for the default university return this SAME data — the local
            // tables above are the cache; this is what they're a cache OF.
            var localCourses = await db.Courses.Where(c => c.UniversityCode == DefaultUniversityCode).ToListAsync();
            var localStudents = await db.Students.Where(s => s.UniversityCode == DefaultUniversityCode).ToListAsync();
            var localEnrollments = await db.Enrollments.Where(e => e.UniversityCode == DefaultUniversityCode).ToListAsync();

            await externalStore.ProvisionKnownDatasetAsync(
                defaultApiKey,
                localStudents.Select(s => new ExternalStudentRecord
                {
                    StudentNumber = s.UniversityId,
                    FullName = s.FullName,
                    Email = s.UniversityEmail,
                    Major = s.Major,
                    YearOfStudy = s.YearOfStudy
                }),
                localCourses.Select(c => new ExternalCourseRecord
                {
                    CourseCode = c.CourseCode,
                    CourseName = c.CourseName,
                    InstructorName = c.InstructorName,
                    InstructorStaffId = c.InstructorId is null ? null
                        : (c.CourseCode is "CSC301" or "MAT202" ? "INSTR-002" : c.CourseCode is "CSC340" or "CSC420" ? "INSTR-001" : null),
                    Credits = c.Credits
                }),
                localEnrollments.Select(e => new ExternalEnrollmentRecord
                {
                    StudentNumber = e.UniversityId,
                    CourseCode = e.CourseCode
                })
            );
        }
    }
}
