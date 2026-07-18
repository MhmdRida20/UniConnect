using Microsoft.AspNetCore.SignalR;

namespace UniConnect.Hubs
{
    /// <summary>
    /// A personal channel, joined by every authenticated user on every page
    /// (see wwwroot/js/global-notifications.js) — unlike the other hubs,
    /// which only matter on their specific feature's page, this needs to
    /// work regardless of what the user is currently looking at, since
    /// system actions (like being removed from a study group) can happen
    /// while they're anywhere in the app, or not connected at all.
    /// </summary>
    public class NotificationHub : Hub
    {
        public async Task JoinUser(string userId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"notify-{userId}");

        public async Task LeaveUser(string userId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"notify-{userId}");
    }
}
