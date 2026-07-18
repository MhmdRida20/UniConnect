using Microsoft.AspNetCore.SignalR;

namespace UniConnect.Hubs
{
    /// <summary>
    /// SignalR hub for live ride location tracking ("Uber-style" tracking — Phase 4).
    ///
    /// Mirrors StudyGroupHub's pattern deliberately: this hub only manages group
    /// membership ("ride-{rideId}"). It does NOT trust the browser to say who the
    /// driver is or push raw location data through hub methods directly — all of
    /// that is validated in RidesController (has the caller actually started this
    /// trip? are they actually the driver?) before the controller pushes an update
    /// into the group via IHubContext&lt;RideTrackingHub&gt;.
    /// </summary>
    public class RideTrackingHub : Hub
    {
        // Called by the browser when it opens a ride's Details page, for both
        // the driver (to broadcast) and accepted passengers (to listen).
        public async Task JoinRideTracking(int rideId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"ride-{rideId}");
        }

        // Called by the browser when it leaves the page (cleanup).
        public async Task LeaveRideTracking(int rideId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ride-{rideId}");
        }

        // "Lobby" group — joined by anyone viewing the Available Rides list, so
        // new/changed rides can push a refresh instead of requiring a manual one.
        public async Task JoinRidesLobby()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "rides-lobby");
        }

        public async Task LeaveRidesLobby()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "rides-lobby");
        }
    }
}
