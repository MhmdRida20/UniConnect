using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Staff-facing side of the Complaints & Ticketing service (UC-06) — the
    /// first screen of UniConnect's web portal. Only "DepartmentStaff" role
    /// accounts can reach this; access is further scoped to the staff
    /// member's own department (E1 of UC-06: "A staff member attempts to
    /// access a ticket outside their department" → denied).
    /// </summary>
    [Authorize(Roles = "DepartmentStaff")]
    public class StaffTicketsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<TicketHub> _hub;
        private readonly UniConnect.Services.AuditLogService _auditLog;

        public StaffTicketsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<TicketHub> hub,
            UniConnect.Services.AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _auditLog = auditLog;
        }

        // ---------- INDEX: department queue, sorted by priority then date (UC-06) --
        public async Task<IActionResult> Index(string? status)
        {
            var staff = await _userManager.GetUserAsync(User);
            if (staff?.Department is null) return Forbid();

            var query = _db.Tickets
                .Include(t => t.Submitter)
                .Include(t => t.Category)
                .Include(t => t.AssignedStaff)
                .Where(t => t.Category!.Name == staff.Department);

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, out var statusFilter))
                query = query.Where(t => t.Status == statusFilter);

            // Priority first (Urgent..Low), then oldest first within a priority.
            var tickets = await query
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync();

            ViewBag.Department = staff.Department;
            ViewBag.SelectedStatus = status;
            ViewBag.CurrentStaffId = staff.Id;
            return View(tickets);
        }

        // ---------- DETAILS ----------------------------------------------------
        public async Task<IActionResult> Details(int id)
        {
            var staff = await _userManager.GetUserAsync(User);
            if (staff?.Department is null) return Forbid();

            var ticket = await _db.Tickets
                .Include(t => t.Category)
                .Include(t => t.Submitter)
                .Include(t => t.AssignedStaff)
                .Include(t => t.Responses.OrderBy(r => r.CreatedAt)).ThenInclude(r => r.Responder)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (ticket is null) return NotFound();

            // E1 of UC-06 — deny access outside the staff member's department.
            if (ticket.Category?.Name != staff.Department) return Forbid();

            // Other department staff for the same list, for reassignment.
            ViewBag.DepartmentColleagues = await _userManager.Users
                .Where(u => u.Department == staff.Department)
                .ToListAsync();
            ViewBag.AllCategories = await _db.TicketCategories
                .Where(c => c.UniversityCode == staff.UniversityCode && c.IsActive)
                .OrderBy(c => c.Name).ToListAsync();
            ViewBag.CurrentStaffId = staff.Id;

            return View(ticket);
        }

        // ---------- PICK UP (claim an unassigned ticket in my department) -----
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PickUp(int id)
        {
            var staff = await _userManager.GetUserAsync(User);
            if (staff?.Department is null) return Forbid();

            var ticket = await _db.Tickets.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return NotFound();
            if (ticket.Category?.Name != staff.Department) return Forbid();

            ticket.AssignedStaffId = staff.Id;
            ticket.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await BroadcastTicketChanged(ticket);
            TempData["Success"] = "Ticket assigned to you.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- RESPOND (add a reply + optionally change status, FR-30) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Respond(int id, string content, TicketStatus? newStatus)
        {
            var staff = await _userManager.GetUserAsync(User);
            if (staff?.Department is null) return Forbid();

            var ticket = await _db.Tickets.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return NotFound();
            if (ticket.Category?.Name != staff.Department) return Forbid();

            if (string.IsNullOrWhiteSpace(content))
            {
                TempData["Error"] = "Please enter a response.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Responding auto-assigns the ticket to you if nobody has it yet —
            // matches the edge case "another staff member in the department
            // can pick up" an unassigned or unavailable colleague's ticket.
            ticket.AssignedStaffId ??= staff.Id;

            TicketStatus? previousStatus = null;
            if (newStatus.HasValue && newStatus.Value != ticket.Status)
            {
                previousStatus = ticket.Status;
                ticket.Status = newStatus.Value;
            }

            _db.TicketResponses.Add(new TicketResponse
            {
                TicketId = id,
                ResponderId = staff.Id,
                Content = content.Trim(),
                PreviousStatus = previousStatus,
                NewStatus = previousStatus.HasValue ? ticket.Status : null,
                CreatedAt = DateTime.UtcNow
            });
            ticket.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (previousStatus.HasValue)
            {
                await _auditLog.LogAsync(
                    "TicketStatusChanged",
                    userId: staff.Id,
                    universityCode: staff.UniversityCode,
                    entityType: "Ticket",
                    entityId: ticket.Id.ToString(),
                    details: $"{previousStatus} -> {ticket.Status}");
            }

            await BroadcastTicketChanged(ticket);
            TempData["Success"] = "Response sent.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- REASSIGN (A1 of UC-06) -------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reassign(int id, int newCategoryId, string? newStaffId)
        {
            var staff = await _userManager.GetUserAsync(User);
            if (staff?.Department is null) return Forbid();

            var ticket = await _db.Tickets.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return NotFound();
            if (ticket.Category?.Name != staff.Department) return Forbid();

            var newCategory = await _db.TicketCategories.FindAsync(newCategoryId);
            if (newCategory is null)
            {
                TempData["Error"] = "Please choose a valid department.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var oldDepartment = ticket.Category!.Name;
            ticket.CategoryId = newCategoryId;
            ticket.AssignedStaffId = string.IsNullOrWhiteSpace(newStaffId) ? null : newStaffId;

            _db.TicketResponses.Add(new TicketResponse
            {
                TicketId = id,
                ResponderId = staff.Id,
                Content = $"Ticket reassigned from {oldDepartment} to {newCategory.Name}.",
                CreatedAt = DateTime.UtcNow
            });
            ticket.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await BroadcastTicketChanged(ticket);
            // Notify the new department's queue too, since the ticket now belongs there.
            await _hub.Clients.Group($"staff-dept-{newCategory.Name}").SendAsync("TicketCreated");

            TempData["Success"] = $"Ticket reassigned to {newCategory.Name}.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- REJECT (A2 of UC-06) ---------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string reason)
        {
            var staff = await _userManager.GetUserAsync(User);
            if (staff?.Department is null) return Forbid();

            var ticket = await _db.Tickets.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return NotFound();
            if (ticket.Category?.Name != staff.Department) return Forbid();

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["Error"] = "Please provide a reason for rejecting this ticket.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var previousStatus = ticket.Status;
            ticket.Status = TicketStatus.Rejected;
            ticket.AssignedStaffId ??= staff.Id;

            _db.TicketResponses.Add(new TicketResponse
            {
                TicketId = id,
                ResponderId = staff.Id,
                Content = reason.Trim(),
                PreviousStatus = previousStatus,
                NewStatus = TicketStatus.Rejected,
                CreatedAt = DateTime.UtcNow
            });
            ticket.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await BroadcastTicketChanged(ticket);
            TempData["Success"] = "Ticket rejected.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- FLAG / UNFLAG OFFENSIVE CONTENT --------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleOffensiveFlag(int id)
        {
            var staff = await _userManager.GetUserAsync(User);
            if (staff?.Department is null) return Forbid();

            var ticket = await _db.Tickets.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return NotFound();
            if (ticket.Category?.Name != staff.Department) return Forbid();

            ticket.IsFlaggedOffensive = !ticket.IsFlaggedOffensive;
            await _db.SaveChangesAsync();

            await BroadcastTicketChanged(ticket);
            TempData["Success"] = ticket.IsFlaggedOffensive
                ? "Ticket flagged for offensive content."
                : "Offensive-content flag removed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task BroadcastTicketChanged(Ticket ticket)
        {
            await _hub.Clients.Group($"ticket-{ticket.Id}").SendAsync("TicketUpdated");
            await _hub.Clients.Group($"notify-user-{ticket.SubmitterId}").SendAsync("TicketUpdated");
            if (ticket.Category is not null)
                await _hub.Clients.Group($"staff-dept-{ticket.Category.Name}").SendAsync("TicketUpdated");
        }
    }
}
