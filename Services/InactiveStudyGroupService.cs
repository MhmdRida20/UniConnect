using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// FR-53: "The system shall mark study groups as inactive when no group
    /// activity occurs for a configured period."
    ///
    /// Runs periodically in the background (not tied to any single request),
    /// scans Active/Full groups, and flips any with no recent activity to
    /// Inactive. "Activity" = the group being created, a member joining, or a
    /// message being posted — whichever is most recent.
    ///
    /// Both the check interval and the inactivity threshold are configurable
    /// via appsettings (see "StudyGroups" section), with sensible defaults so
    /// it works out of the box. For demoing this feature without waiting 30
    /// real days, lower "InactivityThresholdDays" in appsettings.json (e.g. to
    /// a fraction of a day) and "InactivityCheckIntervalMinutes" (e.g. to 1).
    /// </summary>
    public class InactiveStudyGroupService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<InactiveStudyGroupService> _logger;
        private readonly TimeSpan _checkInterval;
        private readonly TimeSpan _inactivityThreshold;

        public InactiveStudyGroupService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<InactiveStudyGroupService> logger)
        {
            _services = services;
            _logger = logger;

            var intervalMinutes = config.GetValue<double?>("StudyGroups:InactivityCheckIntervalMinutes") ?? 60;
            var thresholdDays = config.GetValue<double?>("StudyGroups:InactivityThresholdDays") ?? 30;

            _checkInterval = TimeSpan.FromMinutes(intervalMinutes);
            _inactivityThreshold = TimeSpan.FromDays(thresholdDays);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Small initial delay so this doesn't compete with app startup.
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForInactiveGroupsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while checking for inactive study groups.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        private async Task CheckForInactiveGroupsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<StudyGroupHub>>();

            var cutoff = DateTime.UtcNow - _inactivityThreshold;

            var candidates = await db.StudyGroups
                .Where(g => g.Status == StudyGroupStatus.Active || g.Status == StudyGroupStatus.Full)
                .Select(g => new
                {
                    Group = g,
                    LastMessageAt = g.Messages
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => (DateTime?)m.SentAt)
                        .FirstOrDefault(),
                    LastJoinAt = g.Members
                        .OrderByDescending(m => m.JoinedAt)
                        .Select(m => (DateTime?)m.JoinedAt)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var changedGroupIds = new List<int>();

            foreach (var c in candidates)
            {
                var lastActivity = c.Group.CreatedAt;
                if (c.LastMessageAt.HasValue && c.LastMessageAt.Value > lastActivity)
                    lastActivity = c.LastMessageAt.Value;
                if (c.LastJoinAt.HasValue && c.LastJoinAt.Value > lastActivity)
                    lastActivity = c.LastJoinAt.Value;

                if (lastActivity < cutoff)
                {
                    c.Group.Status = StudyGroupStatus.Inactive;
                    changedGroupIds.Add(c.Group.Id);
                }
            }

            if (changedGroupIds.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Marked {Count} study group(s) as inactive: {Ids}",
                    changedGroupIds.Count, string.Join(", ", changedGroupIds));

                // Live-refresh anyone browsing the list or looking at one of
                // these groups directly.
                await hub.Clients.Group("study-groups-lobby").SendAsync("StudyGroupListChanged", cancellationToken: ct);
                foreach (var id in changedGroupIds)
                    await hub.Clients.Group($"group-{id}").SendAsync("GroupUpdated", cancellationToken: ct);
            }
        }
    }
}
