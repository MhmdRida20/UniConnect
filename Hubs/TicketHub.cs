using Microsoft.AspNetCore.SignalR;

namespace UniConnect.Hubs
{
    /// <summary>
    /// Live updates for the Ticketing service. Same pattern as StudyGroupHub /
    /// RideTrackingHub — this hub only manages group membership; all
    /// authorization and business logic lives in the controllers, which push
    /// updates into these groups via IHubContext&lt;TicketHub&gt; after
    /// validating the request.
    ///
    /// Three kinds of groups:
    ///   "ticket-{id}"        — anyone viewing one specific ticket's Details page
    ///   "notify-user-{id}"   — a student's own tickets list (so status changes
    ///                          on ANY of their tickets show up live)
    ///   "staff-dept-{dept}"  — staff viewing their department's ticket queue
    /// </summary>
    public class TicketHub : Hub
    {
        public async Task JoinTicket(int ticketId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");

        public async Task LeaveTicket(int ticketId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");

        public async Task JoinMyTickets(string userId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"notify-user-{userId}");

        public async Task LeaveMyTickets(string userId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"notify-user-{userId}");

        public async Task JoinDepartmentQueue(string department)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"staff-dept-{department}");

        public async Task LeaveDepartmentQueue(string department)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"staff-dept-{department}");
    }
}
