using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// FR-75: "The system shall mark clubs as inactive when no club activity
    /// occurs for a configured period."
    ///
    /// Mirrors InactiveStudyGroupService exactly — "activity" here is the
    /// most recent of: club creation, a member joining, an announcement, an
    /// event being created, or a chat message.
    ///
    /// Configurable via the same style of appsettings keys, under "Clubs"
    /// instead of "StudyGroups", with the same defaults and the same trick
    /// for testing without waiting 30 real days.
    /// </summary>
    public class InactiveClubService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<InactiveClubService> _logger;
        private readonly TimeSpan _checkInterval;
        private readonly TimeSpan _inactivityThreshold;

        public InactiveClubService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<InactiveClubService> logger)
        {
            _services = services;
            _logger = logger;

            var intervalMinutes = config.GetValue<double?>("Clubs:InactivityCheckIntervalMinutes") ?? 60;
            var thresholdDays = config.GetValue<double?>("Clubs:InactivityThresholdDays") ?? 30;

            _checkInterval = TimeSpan.FromMinutes(intervalMinutes);
            _inactivityThreshold = TimeSpan.FromDays(thresholdDays);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForInactiveClubsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while checking for inactive clubs.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        private async Task CheckForInactiveClubsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ClubHub>>();

            var cutoff = DateTime.UtcNow - _inactivityThreshold;

            var candidates = await db.Clubs
                .Where(c => c.Status == ClubStatus.Active)
                .Select(c => new
                {
                    Club = c,
                    LastJoinAt = c.Members.OrderByDescending(m => m.JoinedAt).Select(m => (DateTime?)m.JoinedAt).FirstOrDefault(),
                    LastAnnouncementAt = c.Announcements.OrderByDescending(a => a.CreatedAt).Select(a => (DateTime?)a.CreatedAt).FirstOrDefault(),
                    LastEventAt = c.Events.OrderByDescending(e => e.CreatedAt).Select(e => (DateTime?)e.CreatedAt).FirstOrDefault(),
                    LastMessageAt = c.Messages.OrderByDescending(m => m.SentAt).Select(m => (DateTime?)m.SentAt).FirstOrDefault()
                })
                .ToListAsync(ct);

            var changedIds = new List<int>();

            foreach (var c in candidates)
            {
                var lastActivity = c.Club.CreatedAt;
                foreach (var candidate in new[] { c.LastJoinAt, c.LastAnnouncementAt, c.LastEventAt, c.LastMessageAt })
                    if (candidate.HasValue && candidate.Value > lastActivity)
                        lastActivity = candidate.Value;

                if (lastActivity < cutoff)
                {
                    c.Club.Status = ClubStatus.Inactive;
                    changedIds.Add(c.Club.Id);
                }
            }

            if (changedIds.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Marked {Count} club(s) as inactive: {Ids}", changedIds.Count, string.Join(", ", changedIds));

                await hub.Clients.Group("clubs-lobby").SendAsync("ClubListChanged", cancellationToken: ct);
                foreach (var id in changedIds)
                    await hub.Clients.Group($"club-{id}").SendAsync("ClubUpdated", cancellationToken: ct);
            }
        }
    }
}
