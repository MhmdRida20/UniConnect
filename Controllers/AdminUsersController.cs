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
    ///
    /// A UniversityAdmin only ever sees/manages accounts belonging to their
    /// own university; a Super Admin ("Admin") sees everyone.
    /// </summary>
    [Authorize(Roles = "Admin,UniversityAdmin")]
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

        private bool IsSuperAdmin => User.IsInRole("Admin");

        public async Task<IActionResult> Index(string? search)
        {
            var query = _db.Users.AsQueryable();

            if (!IsSuperAdmin)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null) return Challenge();
                query = query.Where(u => u.UniversityCode == currentUser.UniversityCode);
            }

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
            ViewBag.IsSuperAdmin = IsSuperAdmin;

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

            // A UniversityAdmin may only suspend/reactivate accounts that
            // belong to their OWN university — never another institution's.
            if (!IsSuperAdmin)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null || user.UniversityCode != currentUser.UniversityCode)
                    return Forbid();
            }

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

        // ---------- CHANGE ROLE — FR-83 ------------------------------------------
        // Deliberately does NOT allow assigning "Admin" or "Company" through
        // this screen: Super Admin accounts are provisioned deliberately
        // (there's exactly one seeded), and Company accounts are tied to
        // the university-creation flow specifically (one per university) —
        // not something to casually reassign here.
        private static readonly string[] SuperAdminAssignableRoles = { "Student", "Instructor", "DepartmentStaff", "UniversityAdmin" };
        private static readonly string[] UniversityAdminAssignableRoles = { "Student", "Instructor", "DepartmentStaff" };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole(string userId, string newRole)
        {
            if (userId == _userManager.GetUserId(User))
            {
                TempData["Error"] = "You can't change your own role.";
                return RedirectToAction(nameof(Index));
            }

            var allowedRoles = IsSuperAdmin ? SuperAdminAssignableRoles : UniversityAdminAssignableRoles;
            if (!allowedRoles.Contains(newRole))
            {
                TempData["Error"] = "That role isn't available to assign from this screen.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return NotFound();

            if (!IsSuperAdmin)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null || user.UniversityCode != currentUser.UniversityCode)
                    return Forbid();
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            // Never touch Admin/Company/UniversityAdmin accounts through
            // this screen either, even to REMOVE those roles — those stay
            // deliberate, out-of-band actions.
            if (currentRoles.Any(r => r is "Admin" or "Company") || (!IsSuperAdmin && currentRoles.Contains("UniversityAdmin")))
            {
                TempData["Error"] = "This account's role can't be changed from this screen.";
                return RedirectToAction(nameof(Index));
            }

            if (currentRoles.Contains(newRole))
            {
                TempData["Error"] = $"{user.FullName} already has the {newRole} role.";
                return RedirectToAction(nameof(Index));
            }

            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, newRole);
            await _userManager.UpdateSecurityStampAsync(user); // role change takes effect within the existing 1-minute revalidation window

            await _auditLog.LogAsync(
                "UserRoleChanged",
                userId: _userManager.GetUserId(User),
                universityCode: user.UniversityCode,
                entityType: "User",
                entityId: user.Id,
                details: $"{user.FullName}: {string.Join(",", currentRoles)} -> {newRole}");

            TempData["Success"] = $"{user.FullName}'s role changed to {newRole}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
