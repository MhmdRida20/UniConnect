using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// Edge Case (Complaints and Ticketing): "Ticket stale for too long — a
    /// ticket remains in Open status without a response for an extended
    /// period. The system shall escalate or flag the ticket for admin
    /// review."
    ///
    /// Runs periodically, flags any Open ticket that hasn't been updated
    /// (no staff response, no status change) within a configurable window.
    /// Flagging (not changing status) was chosen deliberately — a stale
    /// ticket is still exactly as valid as before, it just needs visible
    /// attention, so this doesn't alter the workflow status a student sees.
    /// </summary>
    public class TicketStalenessService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<TicketStalenessService> _logger;
        private readonly TimeSpan _checkInterval;
        private readonly TimeSpan _staleThreshold;

        public TicketStalenessService(
            IServiceProvider services,
            IConfiguration config,
            ILogger<TicketStalenessService> logger)
        {
            _services = services;
            _logger = logger;

            var intervalMinutes = config.GetValue<double?>("Tickets:StalenessCheckIntervalMinutes") ?? 60;
            var thresholdHours = config.GetValue<double?>("Tickets:StaleAfterHours") ?? 48;

            _checkInterval = TimeSpan.FromMinutes(intervalMinutes);
            _staleThreshold = TimeSpan.FromHours(thresholdHours);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForStaleTicketsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while checking for stale tickets.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        private async Task CheckForStaleTicketsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<TicketHub>>();

            var cutoff = DateTime.UtcNow - _staleThreshold;

            var staleTickets = await db.Tickets
                .Where(t => t.Status == TicketStatus.Open && !t.IsEscalated && t.UpdatedAt < cutoff)
                .ToListAsync(ct);

            if (staleTickets.Count == 0) return;

            foreach (var ticket in staleTickets)
            {
                ticket.IsEscalated = true;
                ticket.EscalatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Escalated {Count} stale ticket(s).", staleTickets.Count);

            foreach (var ticket in staleTickets)
                await hub.Clients.Group($"ticket-{ticket.Id}").SendAsync("TicketUpdated", cancellationToken: ct);
        }
    }
}
