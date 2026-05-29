using Microsoft.EntityFrameworkCore;
using UniConnect.Models;

namespace UniConnect.Data
{
    /// <summary>
    /// Seeds the database with MOCK university data: students, courses, and enrollments.
    ///
    /// Why mock data?
    /// In production, the system would call the university's API to fetch the list of
    /// registered students and their enrolled courses. Since we don't have that API
    /// during development, we hardcode realistic data here.
    ///
    /// During signup, the user must enter a University ID. We check it against this
    /// seeded Students table. If it doesn't exist → "you are not a registered student".
    ///
    /// Called from Program.cs at startup. Safe to call repeatedly — it checks
    /// whether data already exists before inserting.
    /// </summary>
    public static class DbSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext db)
        {
            // Make sure the database and all tables exist.
            // (For Code First with migrations, you should run "dotnet ef database update"
            //  instead; we call MigrateAsync below in Program.cs.)
            await db.Database.EnsureCreatedAsync();

            // ---- 1. COURSES -----------------------------------------------------
            if (!await db.Courses.AnyAsync())
            {
                var courses = new[]
                {
                    new Course { CourseCode = "CSC301", CourseName = "Data Structures and Algorithms",
                                 InstructorName = "Dr. Maya Habib", Credits = 3 },
                    new Course { CourseCode = "CSC340", CourseName = "Database Systems",
                                 InstructorName = "Dr. Rami Khoury", Credits = 3 },
                    new Course { CourseCode = "CSC410", CourseName = "Software Engineering",
                                 InstructorName = "Dr. Lina Saad",   Credits = 3 },
                    new Course { CourseCode = "CSC420", CourseName = "Web Development with ASP.NET",
                                 InstructorName = "Dr. Karim Aoun",  Credits = 3 },
                    new Course { CourseCode = "MAT202", CourseName = "Discrete Mathematics",
                                 InstructorName = "Dr. Hala Mansour", Credits = 3 },
                    new Course { CourseCode = "MAT310", CourseName = "Linear Algebra",
                                 InstructorName = "Dr. Joseph Nehme", Credits = 3 },
                    new Course { CourseCode = "PHY201", CourseName = "Physics II",
                                 InstructorName = "Dr. Nadine Fares", Credits = 3 },
                    new Course { CourseCode = "ENG101", CourseName = "Technical Writing",
                                 InstructorName = "Dr. Sami Bechara", Credits = 2 },
                };
                await db.Courses.AddRangeAsync(courses);
                await db.SaveChangesAsync();
            }

            // ---- 2. STUDENTS ----------------------------------------------------
            // These are the IDs students will use during signup to prove they belong
            // to the university. Share these IDs with testers.
            if (!await db.Students.AnyAsync())
            {
                var students = new[]
                {
                    new Student { UniversityId = "U2024001", FullName = "Ali Hassan",
                                  UniversityEmail = "ali.hassan@uni.edu",   Major = "Computer Science", YearOfStudy = 3 },
                    new Student { UniversityId = "U2024002", FullName = "Sara Khalil",
                                  UniversityEmail = "sara.khalil@uni.edu",  Major = "Computer Science", YearOfStudy = 3 },
                    new Student { UniversityId = "U2024003", FullName = "Omar Nassar",
                                  UniversityEmail = "omar.nassar@uni.edu",  Major = "Computer Science", YearOfStudy = 4 },
                    new Student { UniversityId = "U2024004", FullName = "Lara Tannous",
                                  UniversityEmail = "lara.tannous@uni.edu", Major = "Mathematics",      YearOfStudy = 2 },
                    new Student { UniversityId = "U2024005", FullName = "Karim Awad",
                                  UniversityEmail = "karim.awad@uni.edu",   Major = "Computer Science", YearOfStudy = 3 },
                    new Student { UniversityId = "U2024006", FullName = "Nour Daher",
                                  UniversityEmail = "nour.daher@uni.edu",   Major = "Physics",          YearOfStudy = 2 },
                    new Student { UniversityId = "U2024007", FullName = "Hadi Murad",
                                  UniversityEmail = "hadi.murad@uni.edu",   Major = "Computer Science", YearOfStudy = 4 },
                    new Student { UniversityId = "U2024008", FullName = "Rana Saliba",
                                  UniversityEmail = "rana.saliba@uni.edu",  Major = "Computer Science", YearOfStudy = 3 },
                };
                await db.Students.AddRangeAsync(students);
                await db.SaveChangesAsync();
            }

            // ---- 3. ENROLLMENTS ------------------------------------------------
            // Wire each student to a set of courses. This is what UC-08 / FR-23
            // will read to show "my courses" and what FR-19/FR-24 will use to
            // verify "is this student allowed to join this study group?"
            if (!await db.Enrollments.AnyAsync())
            {
                var enrollments = new[]
                {
                    // Ali (CS, year 3) — full schedule
                    new Enrollment { UniversityId = "U2024001", CourseCode = "CSC301" },
                    new Enrollment { UniversityId = "U2024001", CourseCode = "CSC340" },
                    new Enrollment { UniversityId = "U2024001", CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024001", CourseCode = "MAT202" },

                    // Sara (CS, year 3)
                    new Enrollment { UniversityId = "U2024002", CourseCode = "CSC301" },
                    new Enrollment { UniversityId = "U2024002", CourseCode = "CSC340" },
                    new Enrollment { UniversityId = "U2024002", CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024002", CourseCode = "ENG101" },

                    // Omar (CS, year 4)
                    new Enrollment { UniversityId = "U2024003", CourseCode = "CSC410" },
                    new Enrollment { UniversityId = "U2024003", CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024003", CourseCode = "MAT310" },

                    // Lara (Math, year 2)
                    new Enrollment { UniversityId = "U2024004", CourseCode = "MAT202" },
                    new Enrollment { UniversityId = "U2024004", CourseCode = "MAT310" },
                    new Enrollment { UniversityId = "U2024004", CourseCode = "ENG101" },

                    // Karim (CS, year 3)
                    new Enrollment { UniversityId = "U2024005", CourseCode = "CSC301" },
                    new Enrollment { UniversityId = "U2024005", CourseCode = "CSC340" },
                    new Enrollment { UniversityId = "U2024005", CourseCode = "MAT202" },

                    // Nour (Physics, year 2)
                    new Enrollment { UniversityId = "U2024006", CourseCode = "PHY201" },
                    new Enrollment { UniversityId = "U2024006", CourseCode = "MAT310" },
                    new Enrollment { UniversityId = "U2024006", CourseCode = "ENG101" },

                    // Hadi (CS, year 4)
                    new Enrollment { UniversityId = "U2024007", CourseCode = "CSC410" },
                    new Enrollment { UniversityId = "U2024007", CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024007", CourseCode = "CSC340" },

                    // Rana (CS, year 3)
                    new Enrollment { UniversityId = "U2024008", CourseCode = "CSC301" },
                    new Enrollment { UniversityId = "U2024008", CourseCode = "CSC420" },
                    new Enrollment { UniversityId = "U2024008", CourseCode = "MAT202" },
                };
                await db.Enrollments.AddRangeAsync(enrollments);
                await db.SaveChangesAsync();
            }
        }
    }
}
