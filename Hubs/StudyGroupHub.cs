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
    }
}