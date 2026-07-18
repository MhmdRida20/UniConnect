using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Filters;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Student-facing side of the Complaints & Ticketing service (UC-05):
    /// submit a ticket, view your own tickets, reply / follow up.
    /// The staff/web-portal side lives in StaffTicketsController.
    /// </summary>
    [Authorize]
    [RequireService(ServiceCodes.Tickets)]
    public class TicketsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<TicketHub> _hub;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<TicketsController> _logger;
        private readonly UniConnect.Services.AuditLogService _auditLog;

        private const long MaxAttachmentBytes = 5 * 1024 * 1024; // 5 MB (FR-32 / Edge Case E2)
        private static readonly string[] AllowedExtensions =
            { ".pdf", ".png", ".jpg", ".jpeg", ".docx", ".txt" };

        public TicketsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<TicketHub> hub,
            IWebHostEnvironment env,
            ILogger<TicketsController> logger,
            UniConnect.Services.AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _env = env;
            _logger = logger;
            _auditLog = auditLog;
        }

        // ---------- INDEX: my tickets ----------------------------------------------
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var tickets = await _db.Tickets
                .Include(t => t.Category)
                .Include(t => t.AssignedStaff)
                .Where(t => t.SubmitterId == user.Id)
                .OrderByDescending(t => t.UpdatedAt)
                .ToListAsync();

            ViewBag.CurrentUserId = user.Id;
            return View(tickets);
        }

        // ---------- CREATE (GET) ---------------------------------------------------
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var vm = new TicketCreateVM { AvailableCategories = await BuildCategoryList(user.UniversityCode) };
            return View(vm);
        }

        // ---------- CREATE (POST) --------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TicketCreateVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var category = await _db.TicketCategories
                .FirstOrDefaultAsync(c => c.Id == vm.CategoryId && c.UniversityCode == user.UniversityCode && c.IsActive);
            if (category is null)
                ModelState.AddModelError(nameof(vm.CategoryId), "Please choose a valid category.");

            // E2: attachment too large or wrong type — reject without losing the
            // ticket text (the form redisplays with everything else intact).
            string? attachmentPath = null;
            string? attachmentFileName = null;
            if (vm.Attachment is { Length: > 0 })
            {
                var ext = Path.GetExtension(vm.Attachment.FileName).ToLowerInvariant();
                if (vm.Attachment.Length > MaxAttachmentBytes)
                    ModelState.AddModelError(nameof(vm.Attachment), "Attachment must be 5 MB or smaller.");
                else if (!AllowedExtensions.Contains(ext))
                    ModelState.AddModelError(nameof(vm.Attachment), "Allowed file types: PDF, Word, TXT, JPG, PNG.");
            }

            // Edge case: duplicate ticket submission — warn, don't block.
            if (ModelState.IsValid && !vm.ConfirmDuplicate)
            {
                var recentCutoff = DateTime.UtcNow.AddHours(-24);
                var possibleDuplicate = await _db.Tickets.AnyAsync(t =>
                    t.SubmitterId == user.Id &&
                    t.CategoryId == vm.CategoryId &&
                    t.Title == vm.Title &&
                    t.CreatedAt >= recentCutoff);

                if (possibleDuplicate)
                {
                    ViewBag.DuplicateWarning = true;
                    vm.AvailableCategories = await BuildCategoryList(user.UniversityCode);
                    return View(vm);
                }
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(kvp => kvp.Value?.Errors.Count > 0)
                    .Select(kvp => $"{kvp.Key}: {string.Join("; ", kvp.Value!.Errors.Select(e => e.ErrorMessage))}");
                _logger.LogWarning("Ticket Create failed validation: {Errors}", string.Join(" | ", errors));

                vm.AvailableCategories = await BuildCategoryList(user.UniversityCode);
                return View(vm);
            }

            if (vm.Attachment is { Length: > 0 })
            {
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "tickets");
                Directory.CreateDirectory(uploadsDir);
                var storedName = $"{Guid.NewGuid()}{Path.GetExtension(vm.Attachment.FileName)}";
                var fullPath = Path.Combine(uploadsDir, storedName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                    await vm.Attachment.CopyToAsync(stream);

                attachmentPath = $"/uploads/tickets/{storedName}";
                attachmentFileName = Path.GetFileName(vm.Attachment.FileName);
            }

            var ticket = new Ticket
            {
                UniversityCode = user.UniversityCode,
                SubmitterId = user.Id,
                CategoryId = vm.CategoryId,
                Title = vm.Title.Trim(),
                Description = vm.Description.Trim(),
                Priority = vm.Priority,
                Status = TicketStatus.Open,
                AttachmentPath = attachmentPath,
                AttachmentFileName = attachmentFileName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Tickets.Add(ticket);
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "TicketCreated",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "Ticket",
                entityId: ticket.Id.ToString(),
                details: $"Category: {category!.Name}, priority: {ticket.Priority}");

            // Notify the department's staff queue live (FR-33, UC-05 main flow).
            await _hub.Clients.Group($"staff-dept-{category!.Name}").SendAsync("TicketCreated");

            TempData["Success"] = "Your ticket has been submitted.";
            return RedirectToAction(nameof(Details), new { id = ticket.Id });
        }

        // ---------- DETAILS ----------------------------------------------------
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var ticket = await _db.Tickets
                .Include(t => t.Category)
                .Include(t => t.Submitter)
                .Include(t => t.AssignedStaff)
                .Include(t => t.Responses.OrderBy(r => r.CreatedAt)).ThenInclude(r => r.Responder)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (ticket is null) return NotFound();
            if (ticket.SubmitterId != user.Id) return Forbid();

            ViewBag.CurrentUserId = user.Id;
            return View(ticket);
        }

        // ---------- REPLY (student follow-up, A1 of UC-05) --------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(int id, string content)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();
            if (string.IsNullOrWhiteSpace(content))
            {
                TempData["Error"] = "Please enter a message.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var ticket = await _db.Tickets.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return NotFound();
            if (ticket.SubmitterId != user.Id) return Forbid();

            TicketStatus? previousStatus = null;
            TicketStatus? newStatus = null;

            // Edge case: replying to a Closed ticket reopens it and notifies staff.
            if (ticket.Status == TicketStatus.Closed)
            {
                previousStatus = ticket.Status;
                ticket.Status = TicketStatus.Open;
                newStatus = ticket.Status;
            }

            var response = new TicketResponse
            {
                TicketId = id,
                ResponderId = user.Id,
                Content = content.Trim(),
                PreviousStatus = previousStatus,
                NewStatus = newStatus,
                CreatedAt = DateTime.UtcNow
            };
            _db.TicketResponses.Add(response);
            ticket.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"ticket-{id}").SendAsync("TicketUpdated");
            await _hub.Clients.Group($"notify-user-{user.Id}").SendAsync("TicketUpdated");
            if (ticket.Category is not null)
                await _hub.Clients.Group($"staff-dept-{ticket.Category.Name}").SendAsync("TicketUpdated");

            TempData["Success"] = previousStatus is not null
                ? "Your reply was sent and the ticket has been reopened."
                : "Your reply was sent.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task<SelectList> BuildCategoryList(string universityCode)
        {
            var categories = await _db.TicketCategories
                .Where(c => c.UniversityCode == universityCode && c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();
            return new SelectList(categories, "Id", "Name");
        }
    }
}
