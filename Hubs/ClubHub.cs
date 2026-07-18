using Microsoft.AspNetCore.SignalR;

namespace UniConnect.Hubs
{
    /// <summary>
    /// Live updates for the Clubs & Organizations service. Same pattern as
    /// StudyGroupHub — this hub only manages group membership; all
    /// authorization and business logic lives in ClubsController, which
    /// pushes updates into these groups via IHubContext&lt;ClubHub&gt; after
    /// validating the request.
    /// </summary>
    public class ClubHub : Hub
    {
        public async Task JoinClub(int clubId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"club-{clubId}");

        public async Task LeaveClub(int clubId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"club-{clubId}");

        // Personal channel — used to notify a specific member directly (e.g.
        // "your role changed") regardless of which club page they're on.
        public async Task JoinUserNotifications(string userId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"club-user-{userId}");

        public async Task LeaveUserNotifications(string userId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"club-user-{userId}");

        // "Lobby" group — joined by anyone browsing the Clubs list, so a new
        // club, a full club, or a status change can push a refresh.
        public async Task JoinClubsLobby()
            => await Groups.AddToGroupAsync(Context.ConnectionId, "clubs-lobby");

        public async Task LeaveClubsLobby()
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "clubs-lobby");
    }
}
