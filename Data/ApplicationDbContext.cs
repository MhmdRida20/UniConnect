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

        // Mock university data
        public DbSet<Student> Students => Set<Student>();
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<Enrollment> Enrollments => Set<Enrollment>();

        // Study Groups module
        public DbSet<StudyGroup> StudyGroups => Set<StudyGroup>();
        public DbSet<StudyGroupMember> StudyGroupMembers => Set<StudyGroupMember>();
        public DbSet<StudyGroupMessage> StudyGroupMessages => Set<StudyGroupMessage>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // IMPORTANT: must be called first so Identity configures its own tables.
            base.OnModelCreating(builder);

            // ----- ApplicationUser <-> Student (1:1 by UniversityId) -----
            builder.Entity<ApplicationUser>()
                .HasOne(u => u.Student)
                .WithOne()
                .HasForeignKey<ApplicationUser>(u => u.UniversityId)
                .HasPrincipalKey<Student>(s => s.UniversityId)
                .OnDelete(DeleteBehavior.Restrict);

            // Make UniversityId unique on ApplicationUser — one account per student
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.UniversityId)
                .IsUnique();

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
                .HasForeignKey(e => e.CourseCode)
                .OnDelete(DeleteBehavior.Cascade);

            // ----- StudyGroup relationships -----
            builder.Entity<StudyGroup>()
                .HasOne(g => g.Course)
                .WithMany(c => c.StudyGroups)
                .HasForeignKey(g => g.CourseCode)
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
        }
    }
}
