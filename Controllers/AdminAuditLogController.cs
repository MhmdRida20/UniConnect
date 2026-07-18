using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;

namespace UniConnect.Controllers
{
    /// <summary>
    /// FR-92 / UC-20 A1: view recorded system actions, filterable by action
    /// type, user, or date. Admin-only (E1: non-admin access is denied by
    /// the [Authorize] attribute itself).
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdminAuditLogController : Controller
    {
        private readonly ApplicationDbContext _db;
        private const int MaxRows = 500;

        public AdminAuditLogController(ApplicationDbContext db)
        {
            _db = db;
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