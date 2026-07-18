using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Admin screen for suspending/reactivating accounts (Auth Edge Cases:
    /// "Account suspended during active session"). Enforcement itself lives
    /// in SuspendedUserMiddleware — this controller is just the on/off switch.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdminUsersController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuditLogService _auditLog;

        public AdminUsersController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _auditLog = auditLog;
        }

        public async Task<IActionResult> Index(string? search)
        {
            var query = _db.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(u =>
                    u.FullName.Contains(term) ||
                    u.Email!.Contains(term) ||
                    u.UniversityId.Contains(term));
            }

            var users = await query.OrderBy(u => u.FullName).Take(200).ToListAsync();

            var roleNames = new Dictionary<string, List<string>>();
            foreach (var u in users)
                roleNames[u.Id] = (await _userManager.GetRolesAsync(u)).ToList();

            ViewBag.RoleNames = roleNames;
            ViewBag.Search = search;
            ViewBag.CurrentUserId = _userManager.GetUserId(User);

            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSuspend(string userId)
        {
            if (userId == _userManager.GetUserId(User))
            {
                TempData["Error"] = "You can't suspend your own account.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return NotFound();

            user.IsSuspended = !user.IsSuspended;

            // Belt-and-suspenders: invalidating the security stamp means even
            // the periodic revalidation cycle (see Program.cs) would catch
            // this independently of the immediate middleware check.
            await _userManager.UpdateSecurityStampAsync(user);
            await _userManager.UpdateAsync(user);

            await _auditLog.LogAsync(
                user.IsSuspended ? "AccountDeactivation" : "AccountActivation",
                userId: _userManager.GetUserId(User),
                universityCode: user.UniversityCode,
                entityType: "User",
                entityId: user.Id,
                details: $"Target user: {user.FullName} ({user.Email})");

            TempData["Success"] = user.IsSuspended
                ? $"{user.FullName} has been suspended and signed out."
                : $"{user.FullName} has been reactivated.";
            return RedirectToAction(nameof(Index));
        }
    }
}
