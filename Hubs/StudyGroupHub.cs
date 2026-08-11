using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Hubs
{
    /// <summary>
    /// SignalR hub for real-time study group chat.
    ///
    /// Clients connect to "/studygroupHub" (mapped in Program.cs) — browsers
    /// with the auth cookie, the mobile app with a bearer token passed as
    /// ?access_token= (see the JwtBearerEvents in Program.cs, since a WebSocket
    /// cannot set headers). When a client opens a study group's chat it calls
    /// JoinGroup(groupId) to add its connection to a "SignalR group" named
    /// after the study group ID, and any message broadcast there reaches every
    /// connected client instantly.
    ///
    /// Authorisation matters more here than it looks. The REST side refuses
    /// chat history to a non-member (StudyGroupService.GetMessagesAsync returns
    /// Forbidden), but that counts for nothing if the live channel hands the
    /// same messages to anyone who asks: this hub used to accept anonymous
    /// connections and add any caller to any group on request. Both holes are
    /// closed below — the class requires an authenticated caller, and JoinGroup
    /// verifies membership before subscribing anyone to a group's traffic.
    /// </summary>
    /// <remarks>
    /// Both schemes are named explicitly. A bare [Authorize] would use the
    /// application's default, which AddIdentity sets to the Identity cookie —
    /// so a browser would connect and the mobile app, which authenticates with
    /// a bearer token, would be rejected with a 401 and sit on "Offline"
    /// forever. Naming both lets each client in through its own scheme.
    /// </remarks>
    [Authorize(AuthenticationSchemes = AcceptedSchemes)]
    public class StudyGroupHub : Hub
    {
        // IdentityConstants.ApplicationScheme is static readonly rather than
        // const, so it cannot be used in an attribute — hence the literal.
        // StudyGroupHubAuthorizationTests asserts it still matches the real
        // constant, so a rename in ASP.NET Core cannot slip past silently.
        private const string AcceptedSchemes =
            "Identity.Application," + JwtBearerDefaults.AuthenticationScheme;

        private readonly ApplicationDbContext _db;

        public StudyGroupHub(ApplicationDbContext db) => _db = db;

        /// <summary>
        /// Subscribes the caller to a study group's live traffic. Approved
        /// members only, matching who may read the history over REST.
        /// </summary>
        public async Task JoinGroup(int studyGroupId)
        {
            // Both auth schemes put the user id in NameIdentifier, which is
            // what UserIdentifier reads.
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId))
                throw new HubException("You must be signed in to join a study group's chat.");

            var isMember = await _db.StudyGroupMembers.AnyAsync(m =>
                m.StudyGroupId == studyGroupId
                && m.UserId == userId
                && m.Status == MembershipStatus.Approved);

            if (!isMember)
                throw new HubException("You are not a member of this study group.");

            await Groups.AddToGroupAsync(Context.ConnectionId, $"group-{studyGroupId}");
        }

        // Called by the client when it leaves the page (cleanup). No membership
        // check: unsubscribing from something you cannot receive is harmless.
        public async Task LeaveGroup(int studyGroupId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"group-{studyGroupId}");
        }

        // "Lobby" group — joined by anyone viewing the Study Groups list, so a
        // new group, a full group, or a status change can push a refresh
        // instead of requiring a manual one. Open to any signed-in user because
        // what it broadcasts carries no payload: only "the list moved on",
        // after which each client re-reads through the API and sees exactly
        // what it is allowed to see.
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
