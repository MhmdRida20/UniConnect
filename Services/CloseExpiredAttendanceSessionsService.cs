using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// Runs periodically, finds Active attendance sessions whose EndTime has
    /// passed, closes them, and creates an Absent AttendanceRecord for every
    /// enrolled student who never submitted — so attendance history and
    /// reports have a complete, queryable record instead of "no row = absent"
    /// being an implicit rule scattered across every query.
    ///
    /// This talks to the database directly rather than through
    /// IUniversityProvider — it's an internal system process, not a
    /// user-facing service call, and it needs to join Enrollments with real
    /// ApplicationUser accounts (which the adapter deliberately never touches,
    /// since Identity accounts aren't "academic data").
    /// </summary>
    public class CloseExpiredAttendanceSessionsService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<CloseExpiredAttendanceSessionsService> _logger;
        private readonly TimeSpan _checkInterval;

        public CloseExpiredAttendanceSessionsService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<CloseExpiredAttendanceSessionsService> logger)
        {
            _services = services;
            _logger = logger;

            var intervalMinutes = config.GetValue<double?>("Attendance:SessionCloseCheckIntervalMinutes") ?? 5;
            _checkInterval = TimeSpan.FromMinutes(intervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CloseExpiredSessionsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while closing expired attendance sessions.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        private async Task CloseExpiredSessionsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<AttendanceHub>>();

            var now = DateTime.UtcNow;
            var expiredSessions = await db.AttendanceSessions
                .Where(s => s.Status == AttendanceSessionStatus.Active && s.EndTime < now)
                .ToListAsync(ct);

            foreach (var session in expiredSessions)
            {
                // Every enrolled student, matched to a real (registered) account.
                var enrolledUserIds = await db.Enrollments
                    .Where(e => e.UniversityCode == session.UniversityCode && e.CourseCode == session.CourseCode)
                    .Join(db.Users,
                        e => new { e.UniversityCode, e.UniversityId },
                        u => new { u.UniversityCode, UniversityId = u.UniversityId },
                        (e, u) => u.Id)
                    .ToListAsync(ct);

                var submittedUserIds = await db.AttendanceRecords
                    .Where(r => r.AttendanceSessionId == session.Id)
                    .Select(r => r.UserId)
                    .ToListAsync(ct);

                var absentUserIds = enrolledUserIds.Except(submittedUserIds).ToList();

                foreach (var userId in absentUserIds)
                {
                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        AttendanceSessionId = session.Id,
                        UserId = userId,
                        Status = AttendanceStatus.Absent,
                        SubmittedAt = null
                    });
                }

                session.Status = AttendanceSessionStatus.Closed;
            }

            if (expiredSessions.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Closed {Count} expired attendance session(s).", expiredSessions.Count);

                foreach (var session in expiredSessions)
                    await hub.Clients.Group($"attendance-session-{session.Id}").SendAsync("SessionClosed", cancellationToken: ct);
            }
        }
    }
}
