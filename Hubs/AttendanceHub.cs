using Microsoft.AspNetCore.SignalR;

namespace UniConnect.Hubs
{
    /// <summary>
    /// Live updates for an attendance session — lets the instructor watch
    /// checkmarks appear on their roster in real time as students scan,
    /// without a manual refresh. Same pattern as the other hubs: this class
    /// only manages group membership; AttendanceController pushes updates
    /// after validating each submission.
    /// </summary>
    public class AttendanceHub : Hub
    {
        public async Task JoinSession(int sessionId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"attendance-session-{sessionId}");

        public async Task LeaveSession(int sessionId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"attendance-session-{sessionId}");
    }
}
