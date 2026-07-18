using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using UniConnect.Models;

namespace UniConnect.Data
{
    /// <summary>
    /// EF Core database context. Inherits from IdentityDbContext so the Identity
    /// tables (AspNetUsers, AspNetRoles, AspNetUserRoles, etc.) are automatically
    /// included AND so that our ApplicationUser replaces the default IdentityUser.
    ///
    /// All our application tables are declared as DbSet&lt;T&gt; properties.
    /// </summary>
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Multi-university adapter core
        public DbSet<University> Universities => Set<University>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<UniversityService> UniversityServices => Set<UniversityService>();

        // University adapter data (synced from each university's API)
        public DbSet<Student> Students => Set<Student>();
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<Enrollment> Enrollments => Set<Enrollment>();

        // Study Groups module
        public DbSet<StudyGroup> StudyGroups => Set<StudyGroup>();
        public DbSet<StudyGroupMember> StudyGroupMembers => Set<StudyGroupMember>();
        public DbSet<StudyGroupMessage> StudyGroupMessages => Set<StudyGroupMessage>();
        // Ride Sharing module
        public DbSet<Ride> Rides => Set<Ride>();
        public DbSet<RideRequest> RideRequests => Set<RideRequest>();

        // Complaints & Ticketing module
        public DbSet<TicketCategory> TicketCategories => Set<TicketCategory>();
        public DbSet<Ticket> Tickets => Set<Ticket>();
        public DbSet<TicketResponse> TicketResponses => Set<TicketResponse>();

        // Clubs & Organizations module
        public DbSet<Club> Clubs => Set<Club>();
        public DbSet<ClubMember> ClubMembers => Set<ClubMember>();
        public DbSet<ClubAnnouncement> ClubAnnouncements => Set<ClubAnnouncement>();
        public DbSet<ClubEvent> ClubEvents => Set<ClubEvent>();
        public DbSet<EventRsvp> EventRsvps => Set<EventRsvp>();
        public DbSet<ClubMessage> ClubMessages => Set<ClubMessage>();

        // Smart Attendance module
        public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
        public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

        // Notifications module
        public DbSet<Notification> Notifications => Set<Notification>();

        // Persisted external API simulator data (see Models/ExternalSimData.cs)
        public DbSet<ExternalSimCourse> ExternalSimCourses => Set<ExternalSimCourse>();
        public DbSet<ExternalSimStudent> ExternalSimStudents => Set<ExternalSimStudent>();
        public DbSet<ExternalSimEnrollment> ExternalSimEnrollments => Set<ExternalSimEnrollment>();

        // Internship and Career Matching module
        public DbSet<Company> Companies => Set<Company>();
        public DbSet<CareerProfile> CareerProfiles => Set<CareerProfile>();
        public DbSet<StudentSkill> StudentSkills => Set<StudentSkill>();
        public DbSet<Internship> Internships => Set<Internship>();
        public DbSet<InternshipApplication> InternshipApplications => Set<InternshipApplication>();

        // Reporting, Administration, and Audit module
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // IMPORTANT: must be called first so Identity configures its own tables.
            base.OnModelCreating(builder);

            // NOTE: there used to be a strict 1:1 FK here tying every
            // ApplicationUser.UniversityId to a Student row. That broke
            // staff accounts, which aren't students and have no Student
            // record — removed. UniversityId still functions as a natural
            // "ID number" for lookups; it's just not DB-enforced against
            // Students anymore. The uniqueness constraint below still holds
            // (one account per ID number, student or staff).

            // Make UniversityId unique on ApplicationUser — one account per student
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.UniversityId)
                .IsUnique();

            // ----- University (multi-tenant adapter core) -----
            builder.Entity<Student>()
                .HasOne(s => s.University)
                .WithMany(un => un.Students)
                .HasForeignKey(s => s.UniversityCode)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ApplicationUser>()
                .HasOne(u => u.University)
                .WithMany(un => un.Users)
                .HasForeignKey(u => u.UniversityCode)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- Service catalog + per-university enablement -----
            builder.Entity<UniversityService>()
                .HasIndex(us => new { us.UniversityCode, us.ServiceCode })
                .IsUnique();

            builder.Entity<UniversityService>()
                .HasOne(us => us.University)
                .WithMany()
                .HasForeignKey(us => us.UniversityCode)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UniversityService>()
                .HasOne(us => us.Service)
                .WithMany(s => s.UniversityServices)
                .HasForeignKey(us => us.ServiceCode)
                .OnDelete(DeleteBehavior.Cascade);

            // ----- Course (composite key: each university has its own catalog) -----
            builder.Entity<Course>()
                .HasKey(c => new { c.UniversityCode, c.CourseCode });

            builder.Entity<Course>()
                .HasOne(c => c.University)
                .WithMany()
                .HasForeignKey(c => c.UniversityCode)
                .OnDelete(DeleteBehavior.Cascade);

            // ----- Enrollment (composite uniqueness: student cannot enroll twice in same course) -----
            builder.Entity<Enrollment>()
                .HasIndex(e => new { e.UniversityId, e.CourseCode })
                .IsUnique();

            builder.Entity<Enrollment>()
                .HasOne(e => e.Student)
                .WithMany(s => s.Enrollments)
                .HasForeignKey(e => e.UniversityId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Enrollment>()
                .HasOne(e => e.Course)
                .WithMany(c => c.Enrollments)
                .HasForeignKey(e => new { e.UniversityCode, e.CourseCode })
                .OnDelete(DeleteBehavior.Cascade);

            // ----- StudyGroup relationships -----
            builder.Entity<StudyGroup>()
                .HasOne(g => g.Course)
                .WithMany(c => c.StudyGroups)
                .HasForeignKey(g => new { g.UniversityCode, g.CourseCode })
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<StudyGroup>()
                .HasOne(g => g.Creator)
                .WithMany(u => u.CreatedStudyGroups)
                .HasForeignKey(g => g.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- StudyGroupMember (a user can only be in a group once) -----
            builder.Entity<StudyGroupMember>()
                .HasIndex(m => new { m.StudyGroupId, m.UserId })
                .IsUnique();

            builder.Entity<StudyGroupMember>()
                .HasOne(m => m.StudyGroup)
                .WithMany(g => g.Members)
                .HasForeignKey(m => m.StudyGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<StudyGroupMember>()
                .HasOne(m => m.User)
                .WithMany(u => u.StudyGroupMemberships)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- StudyGroupMessage -----
            builder.Entity<StudyGroupMessage>()
                .HasOne(m => m.StudyGroup)
                .WithMany(g => g.Messages)
                .HasForeignKey(m => m.StudyGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<StudyGroupMessage>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- Ride relationships -----
            builder.Entity<Ride>()
                .HasOne(r => r.University)
                .WithMany()
                .HasForeignKey(r => r.UniversityCode)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Ride>()
                .HasOne(r => r.Driver)
                .WithMany()
                .HasForeignKey(r => r.DriverId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<RideRequest>()
                .HasOne(rr => rr.Ride)
                .WithMany(r => r.Requests)
                .HasForeignKey(rr => rr.RideId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<RideRequest>()
                .HasOne(rr => rr.Passenger)
                .WithMany()
                .HasForeignKey(rr => rr.PassengerId)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- Ticket relationships -----
            builder.Entity<TicketCategory>()
                .HasOne(tc => tc.University)
                .WithMany()
                .HasForeignKey(tc => tc.UniversityCode)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Ticket>()
                .HasOne(t => t.University)
                .WithMany()
                .HasForeignKey(t => t.UniversityCode)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Ticket>()
                .HasOne(t => t.Submitter)
                .WithMany()
                .HasForeignKey(t => t.SubmitterId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Ticket>()
                .HasOne(t => t.AssignedStaff)
                .WithMany()
                .HasForeignKey(t => t.AssignedStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Ticket>()
                .HasOne(t => t.Category)
                .WithMany(tc => tc.Tickets)
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<TicketResponse>()
                .HasOne(tr => tr.Ticket)
                .WithMany(t => t.Responses)
                .HasForeignKey(tr => tr.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TicketResponse>()
                .HasOne(tr => tr.Responder)
                .WithMany()
                .HasForeignKey(tr => tr.ResponderId)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- Club relationships -----
            builder.Entity<Club>()
                .HasOne(c => c.University)
                .WithMany()
                .HasForeignKey(c => c.UniversityCode)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Club>()
                .HasOne(c => c.Creator)
                .WithMany()
                .HasForeignKey(c => c.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ClubMember>()
                .HasIndex(m => new { m.ClubId, m.UserId })
                .IsUnique();

            builder.Entity<ClubMember>()
                .HasOne(m => m.Club)
                .WithMany(c => c.Members)
                .HasForeignKey(m => m.ClubId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ClubMember>()
                .HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ClubAnnouncement>()
                .HasOne(a => a.Club)
                .WithMany(c => c.Announcements)
                .HasForeignKey(a => a.ClubId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ClubAnnouncement>()
                .HasOne(a => a.Author)
                .WithMany()
                .HasForeignKey(a => a.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ClubEvent>()
                .HasOne(e => e.Club)
                .WithMany(c => c.Events)
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ClubEvent>()
                .HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<EventRsvp>()
                .HasIndex(r => new { r.ClubEventId, r.UserId })
                .IsUnique();

            builder.Entity<EventRsvp>()
                .HasOne(r => r.ClubEvent)
                .WithMany(e => e.Rsvps)
                .HasForeignKey(r => r.ClubEventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<EventRsvp>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ClubMessage>()
                .HasOne(m => m.Club)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ClubId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ClubMessage>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- Course.InstructorId (added for Attendance) -----
            builder.Entity<Course>()
                .HasOne(c => c.Instructor)
                .WithMany()
                .HasForeignKey(c => c.InstructorId)
                .OnDelete(DeleteBehavior.SetNull);

            // ----- Attendance relationships -----
            builder.Entity<AttendanceSession>()
                .HasOne(s => s.University)
                .WithMany()
                .HasForeignKey(s => s.UniversityCode)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttendanceSession>()
                .HasOne(s => s.Instructor)
                .WithMany()
                .HasForeignKey(s => s.InstructorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttendanceSession>()
                .HasIndex(s => s.QrToken)
                .IsUnique();

            builder.Entity<AttendanceRecord>()
                .HasIndex(r => new { r.AttendanceSessionId, r.UserId })
                .IsUnique();

            builder.Entity<AttendanceRecord>()
                .HasOne(r => r.Session)
                .WithMany(s => s.Records)
                .HasForeignKey(r => r.AttendanceSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<AttendanceRecord>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- Notification -----
            builder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ----- External API simulator persisted data -----
            // Composite keys of (ApiKey, natural-key) — deliberately NOT
            // linked via FK to University, since a real external partner's
            // database wouldn't know or care about UniConnect's own tables.
            builder.Entity<ExternalSimCourse>()
                .HasKey(c => new { c.ApiKey, c.CourseCode });

            builder.Entity<ExternalSimStudent>()
                .HasKey(s => new { s.ApiKey, s.StudentNumber });

            builder.Entity<ExternalSimEnrollment>()
                .HasKey(e => new { e.ApiKey, e.StudentNumber, e.CourseCode });

            // ----- Internship and Career Matching -----
            builder.Entity<Company>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Company>()
                .HasOne(c => c.University)
                .WithMany()
                .HasForeignKey(c => c.UniversityCode)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Company>()
                .HasIndex(c => c.UniversityCode)
                .IsUnique(); // exactly one posting account per university

            builder.Entity<CareerProfile>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<CareerProfile>()
                .HasIndex(p => p.UserId)
                .IsUnique(); // one profile per student

            builder.Entity<StudentSkill>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Internship>()
                .HasOne(i => i.Company)
                .WithMany(c => c.Internships)
                .HasForeignKey(i => i.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<InternshipApplication>()
                .HasIndex(a => new { a.InternshipId, a.UserId })
                .IsUnique(); // Edge Case: "Duplicate application"

            builder.Entity<InternshipApplication>()
                .HasOne(a => a.Internship)
                .WithMany(i => i.Applications)
                .HasForeignKey(a => a.InternshipId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<InternshipApplication>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ----- AuditLog -----
            builder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
