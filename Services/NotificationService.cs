using Microsoft.AspNetCore.SignalR;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// One place to "tell a user what happened" — persists a Notification
    /// row (so they see it later, in /Notifications, even if offline right
    /// now) AND broadcasts it live over NotificationHub (so if they're
    /// online, they see a toast immediately instead of just noticing
    /// something changed with no explanation).
    /// </summary>
    public class NotificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<NotificationHub> _hub;

        public NotificationService(ApplicationDbContext db, IHubContext<NotificationHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        public async Task NotifyAsync(string userId, string title, string message, string? link = null)
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Link = link,
                CreatedAt = DateTime.UtcNow
            };
            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"notify-{userId}").SendAsync("NewNotification", new
            {
                id = notification.Id,
                title = notification.Title,
                message = notification.Message,
                link = notification.Link
            });
        }
    }
}
