using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Controllers
{
    /// <summary>
    /// FR-92 / UC-20 A1: view recorded system actions, filterable by action
    /// type, user, or date.
    ///
    /// A UniversityAdmin only ever sees audit entries scoped to their own
    /// university (forced server-side, not trusted from the request); a
    /// Super Admin ("Admin") sees everything. Non-admin access is denied by
    /// the [Authorize] attribute itself (E1).
    /// </summary>
    [Authorize(Roles = "Admin,UniversityAdmin")]
    public class AdminAuditLogController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int MaxRows = 500;

        public AdminAuditLogController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // "action" collides with ASP.NET Core's own routing value of the
        // same name (every request already carries an "action" = "Index"
        // route value) — [FromQuery(Name = "action")] keeps the querystring
        // parameter name (?action=...) and the dropdown's name="action"
        // working exactly the same, while the actual C# variable is
        // renamed to actionType so it no longer gets silently overwritten.
        public async Task<IActionResult> Index([FromQuery(Name = "action")] string? actionType, string? userId, DateTime? from, DateTime? to)
        {
            var query = _db.AuditLogs.Include(a => a.User).AsQueryable();

            if (!User.IsInRole("Admin"))
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null) return Challenge();
                query = query.Where(a => a.UniversityCode == currentUser.UniversityCode);
            }

            if (!string.IsNullOrWhiteSpace(actionType))
                query = query.Where(a => a.Action == actionType);
            if (!string.IsNullOrWhiteSpace(userId))
                query = query.Where(a => a.UserId == userId);
            if (from.HasValue)
                query = query.Where(a => a.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(a => a.Timestamp < to.Value.AddDays(1));

            var logs = await query.OrderByDescending(a => a.Timestamp).Take(MaxRows).ToListAsync();

            ViewBag.Truncated = logs.Count >= MaxRows;
            ViewBag.ActionTypes = await _db.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
            ViewBag.SelectedAction = actionType;
            ViewBag.SelectedUserId = userId;
            ViewBag.From = from;
            ViewBag.To = to;

            return View(logs);
        }
    }
}
