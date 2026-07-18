using Microsoft.AspNetCore.SignalR;

namespace UniConnect.Hubs
{
    /// <summary>
    /// SignalR hub for real-time study group chat.
    ///
    /// Browsers connect to "/studygroupHub" (mapped in Program.cs).
    /// When a user opens a study group's Details page, the page's JavaScript
    /// calls JoinGroup(groupId) to add their connection to a "SignalR group"
    /// named after the study group ID. After that, any message broadcast to
    /// that group reaches every connected browser in it — instantly.
    /// </summary>
    public class StudyGroupHub : Hub
    {
        // Called by the browser when it opens a study group's chat.
        public async Task JoinGroup(int studyGroupId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"group-{studyGroupId}");
        }

        // Called by the browser when it leaves the page (cleanup).
        public async Task LeaveGroup(int studyGroupId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"group-{studyGroupId}");
        }

        // "Lobby" group — joined by anyone viewing the Study Groups list, so a
        // new group, a full group, or a status change can push a refresh
        // instead of requiring a manual one.
        public async Task JoinStudyGroupsLobby()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "study-groups-lobby");
        }

        public async Task LeaveStudyGroupsLobby()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "study-groups-lobby");
        }
    }
}